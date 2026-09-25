using System.Text.RegularExpressions;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Lessons;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Controllers;

/// <summary>
/// Lessons, and the notes, progress and tutor that hang off one.
///
/// The controller does no work beyond turning a route into a request: it validates the shape of
/// the route values, picks the command or query, and lets the pipeline do the rest. Everything it
/// used to decide now lives in a handler, where it can be reached from anywhere and not just from
/// an HTTP call.
/// </summary>
[ApiController]
[Route("api/lessons")]
// Every action, including the two that look like plain reads: opening a lesson records a view
// against the learner (LessonQueries.RecordViewAsync), and listing them reads per-user mastery.
[Authorize]
[Produces("application/json")]
public partial class LessonsController(IMediator mediator) : ControllerBase
{
    /// <summary>The signed-in account. [Authorize] above guarantees there is one.</summary>
    private string UserKey => User.GetUserId();

    /// <summary>
    /// The curriculum for a certification, ordered by what this learner should study next.
    /// Reads seeded data and the caller's own history only, so it carries no rate limit.
    /// </summary>
    [HttpGet("{certificationCode}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LessonsResponse>> List(string certificationCode, CancellationToken ct)
    {
        if (!IsValidCode(certificationCode)) return InvalidRoute();

        return Ok(await mediator.SendAsync(new ListLessonsQuery(certificationCode, UserKey), ct));
    }

    /// <summary>
    /// One lesson, read from the database. Never calls the AI provider: a topic whose notes have
    /// not been written yet returns a null body, and the client asks for them with the POST below.
    /// </summary>
    [HttpGet("{certificationCode}/{slug}", Name = nameof(GetLesson))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LessonDetailDto>> GetLesson(
        string certificationCode, string slug, CancellationToken ct)
    {
        if (!IsValidCode(certificationCode) || !IsValidSlug(slug)) return InvalidRoute();

        return Ok(await mediator.SendAsync(new GetLessonQuery(certificationCode, slug, UserKey), ct));
    }

    /// <summary>
    /// Writes the notes for a lesson that does not have them yet. Rate limited, because this is
    /// the call that costs a provider request. A topic that already has notes is returned as-is
    /// rather than rewritten, so this is safe to call on every first open.
    /// </summary>
    [HttpPost("{certificationCode}/{slug}/notes")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LessonDetailDto>> WriteNotes(
        string certificationCode, string slug, CancellationToken ct)
    {
        if (!IsValidCode(certificationCode) || !IsValidSlug(slug)) return InvalidRoute();

        return Ok(await mediator.SendAsync(
            new WriteLessonNotesCommand(certificationCode, slug, UserKey, ReplaceExisting: false), ct));
    }

    /// <summary>
    /// Rewrites a lesson's notes, discarding the cached copy. Admin-gated: lesson notes are shared
    /// by every learner, so a rewrite replaces what everyone else reads.
    /// </summary>
    [HttpPut("{certificationCode}/{slug}/notes")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [ServiceFilter(typeof(AdminOnlyFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LessonDetailDto>> RewriteNotes(
        string certificationCode, string slug, CancellationToken ct)
    {
        if (!IsValidCode(certificationCode) || !IsValidSlug(slug)) return InvalidRoute();

        return Ok(await mediator.SendAsync(
            new WriteLessonNotesCommand(certificationCode, slug, UserKey, ReplaceExisting: true), ct));
    }

    /// <summary>
    /// Asks a question about one lesson. The lesson's verified facts and its notes go into the
    /// prompt, so the question arrives with its context already attached.
    /// </summary>
    [HttpPost("{certificationCode}/{slug}/ask")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TutorAnswerDto>> Ask(
        string certificationCode, string slug, [FromBody] TutorAskRequest request, CancellationToken ct)
    {
        if (!IsValidCode(certificationCode) || !IsValidSlug(slug)) return InvalidRoute();

        return Ok(await mediator.SendAsync(
            new AskTutorCommand(certificationCode, slug, request.Question, request.History), ct));
    }

    /// <summary>Marks a lesson complete, or clears that mark. Scoped to the calling learner.</summary>
    [HttpPut("{certificationCode}/{slug}/progress")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<LessonProgressDto>> SetProgress(
        string certificationCode,
        string slug,
        [FromBody] LessonProgressRequest request,
        CancellationToken ct)
    {
        if (!IsValidCode(certificationCode) || !IsValidSlug(slug)) return InvalidRoute();

        return Ok(await mediator.SendAsync(
            new SetLessonProgressCommand(certificationCode, slug, UserKey, request.Completed), ct));
    }

    private ActionResult InvalidRoute() =>
        BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid route",
            Detail = "The certification code or lesson slug is not in a valid format.",
            Instance = HttpContext.Request.Path,
        });

    // Route values reach the database as parameters, so these guards are not an injection defence.
    // They bound what an unauthenticated caller can push through: without them any string of any
    // length becomes a query and a log line, which is free work for anyone who wants to waste it.
    private static bool IsValidCode(string value) => CodePattern().IsMatch(value);

    private static bool IsValidSlug(string value) => SlugPattern().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z]{2,4}-[A-Za-z]?\d{1,3}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,98}[a-z0-9]$")]
    private static partial Regex SlugPattern();
}
