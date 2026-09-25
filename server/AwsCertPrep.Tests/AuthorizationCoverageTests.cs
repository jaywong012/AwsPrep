using System.Reflection;
using AwsCertPrep.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AwsCertPrep.Tests;

/// <summary>
/// Which endpoints need an account, asserted by reflection rather than by reading the source.
///
/// Program.cs sets a fallback policy, so a new action is authenticated unless it says otherwise -
/// which means the dangerous mistake is not forgetting <c>[Authorize]</c>, it is reaching for
/// <c>[AllowAnonymous]</c> to make something work and never revisiting it. These tests are the
/// place that decision has to be restated, six months from now, by someone who has to change a
/// test to change the rule.
///
/// No host and no database: this reads attributes off the assembly.
/// </summary>
public class AuthorizationCoverageTests
{
    private static IEnumerable<Type> Controllers() =>
        typeof(AuthController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

    private static bool Anonymous(MemberInfo member) =>
        member.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

    private static bool Authorized(MemberInfo member) =>
        member.GetCustomAttribute<AuthorizeAttribute>() is not null;

    [Fact]
    public void Every_action_states_whether_it_needs_an_account()
    {
        var undeclared = new List<string>();

        foreach (var controller in Controllers())
        {
            foreach (var action in Actions(controller))
            {
                var declared = Authorized(action) || Anonymous(action)
                               || Authorized(controller) || Anonymous(controller);

                if (!declared) undeclared.Add($"{controller.Name}.{action.Name}");
            }
        }

        Assert.True(undeclared.Count == 0,
            "These actions declare neither [Authorize] nor [AllowAnonymous], so they inherit the "
            + "fallback policy by accident rather than by decision: " + string.Join(", ", undeclared));
    }

    [Theory]
    [InlineData(typeof(ExamsController))]
    [InlineData(typeof(InsightsController))]
    // Lessons looks like a read but is not: opening one records a view against the learner.
    [InlineData(typeof(LessonsController))]
    public void Per_learner_controllers_require_an_account(Type controller)
    {
        Assert.True(Authorized(controller), $"{controller.Name} serves one learner's data and must require sign-in.");
        Assert.False(Anonymous(controller));
    }

    [Fact]
    public void The_certification_catalogue_is_readable_without_an_account()
    {
        // Seeded, shared and read-only, and the SPA needs it before anyone can sign in.
        Assert.True(Anonymous(typeof(CertificationsController)));
    }

    [Theory]
    [InlineData(nameof(QuestionsController.Browse))]
    [InlineData(nameof(QuestionsController.Audit))]
    [InlineData(nameof(QuestionsController.PreviewDifficulty))]
    public void Reading_the_question_bank_needs_no_account(string action)
    {
        // Shared content rather than anyone's personal data. Deliberate: the bank is browsable
        // without registering.
        Assert.True(Anonymous(typeof(QuestionsController).GetMethod(action)!));
    }

    /// <summary>
    /// The trap that attribute-presence tests miss.
    ///
    /// <see cref="AllowAnonymousAttribute"/> on a controller cannot be narrowed by
    /// <see cref="AuthorizeAttribute"/> on one of its actions. The authorization middleware skips
    /// any endpoint carrying IAllowAnonymous metadata, wherever it came from, so the action-level
    /// attribute is inert and the endpoint is open. This shipped once: a valid administrator key
    /// with no bearer token reached DELETE /api/questions/{id} and got a 404 rather than a 401.
    ///
    /// So: a controller that is anonymous as a whole may not contain an action pretending to be
    /// protected. Open the individual actions instead.
    /// </summary>
    [Fact]
    public void An_anonymous_controller_has_no_action_claiming_to_require_an_account()
    {
        var inert = new List<string>();

        foreach (var controller in Controllers().Where(Anonymous))
        {
            foreach (var action in Actions(controller).Where(Authorized))
            {
                inert.Add($"{controller.Name}.{action.Name}");
            }
        }

        Assert.True(inert.Count == 0,
            "[Authorize] on these actions does nothing, because their controller carries "
            + "[AllowAnonymous] and that wins for the whole endpoint: " + string.Join(", ", inert));
    }

    [Theory]
    [InlineData(nameof(QuestionsController.Generate))]        // spends LLM quota
    [InlineData(nameof(QuestionsController.Delete))]          // destroys shared data
    [InlineData(nameof(QuestionsController.RecalculateDifficulty))]
    [InlineData(nameof(QuestionsController.Clean))]
    public void Writing_to_the_question_bank_requires_an_account(string action)
    {
        var method = typeof(QuestionsController).GetMethod(action);

        Assert.NotNull(method);
        Assert.True(Authorized(method!),
            $"QuestionsController.{action} changes shared data or spends quota.");

        // And nothing above it may re-open the endpoint.
        Assert.False(Anonymous(method!));
        Assert.False(Anonymous(typeof(QuestionsController)));
    }

    [Theory]
    [InlineData(nameof(AuthController.Register))]
    [InlineData(nameof(AuthController.Login))]
    public void Getting_a_token_does_not_require_one(string action)
    {
        Assert.True(Anonymous(typeof(AuthController).GetMethod(action)!));
    }

    [Theory]
    [InlineData(nameof(AuthController.Me))]
    [InlineData(nameof(AuthController.Refresh))]
    public void Using_a_token_requires_one(string action)
    {
        Assert.True(Authorized(typeof(AuthController).GetMethod(action)!));
    }
}
