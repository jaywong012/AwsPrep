using System.Diagnostics;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Auth;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Controllers;

/// <summary>
/// Accounts and access tokens.
///
/// Registration is open: anyone who can reach the API can create an account. That is a deliberate
/// choice for a study app, and it means the network perimeter still matters - see the deployment
/// notes in the README.
///
/// There is no logout endpoint. A bearer token is stateless, so signing out is the client
/// deleting it; an endpoint that accepted the call and did nothing would only imply a revocation
/// this API cannot perform.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(
    IMediator mediator,
    UserManager<AppUser> users,
    JwtTokenService tokens) : ControllerBase
{
    /// <summary>Creates an account and returns a token for it.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResultDto>> Register(CredentialsRequest request, CancellationToken ct)
    {
        var outcome = await mediator.SendAsync(new RegisterCommand(request.Email, request.Password), ct);

        return outcome.Success is { } created
            ? Created("/api/auth/me", created)
            : Refusal(outcome);
    }

    /// <summary>Exchanges credentials for a token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResultDto>> Login(CredentialsRequest request, CancellationToken ct)
    {
        var outcome = await mediator.SendAsync(new LoginCommand(request.Email, request.Password), ct);

        return outcome.Success is { } signedIn ? Ok(signedIn) : Refusal(outcome);
    }

    /// <summary>
    /// Turns a refused attempt into a problem+json response, matching every other error here.
    ///
    /// A refusal is a return value rather than an exception, so it never reaches the exception
    /// middleware - which is the point. A mistyped password should not write a stack trace at
    /// error level, least of all when anyone can produce one on demand.
    /// </summary>
    private ObjectResult Refusal(AuthOutcome outcome)
    {
        var (status, title) = outcome.Failure switch
        {
            // 429 rather than 423: it matches the vocabulary the rate limiter already uses, and
            // "wait and try again" is what both mean to the caller.
            AuthFailure.LockedOut => (StatusCodes.Status429TooManyRequests, "Too many attempts"),
            AuthFailure.Rejected => (StatusCodes.Status400BadRequest, "Registration refused"),
            _ => (StatusCodes.Status401Unauthorized, "Sign-in failed"),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = outcome.Message,
            Instance = HttpContext.Request.Path,
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    /// <summary>
    /// Who the bearer token belongs to. The SPA calls this once on start-up to turn a stored
    /// token back into a session, which is also what proves the token is still good.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentUserDto>> Me()
    {
        var user = await users.FindByIdAsync(User.GetUserId());

        // The token validated, so the account existed when it was issued. It not existing now
        // means it was deleted mid-session: treat that as unauthenticated rather than 404, so the
        // SPA signs out through its normal path.
        if (user is null) return Unauthorized();

        return Ok(new CurrentUserDto(user.Id, user.Email!));
    }

    /// <summary>
    /// Renews a still-valid token. Straight past the mediator: there is no rule to enforce beyond
    /// the one <c>[Authorize]</c> already applied.
    ///
    /// This is what stands in for a refresh token. A refresh token kept in localStorage is no
    /// less exposed than the access token it mints, so it would buy revocation and nothing else -
    /// and there is nothing here to revoke it from.
    /// </summary>
    [HttpPost("refresh")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResultDto>> Refresh()
    {
        var user = await users.FindByIdAsync(User.GetUserId());
        if (user is null) return Unauthorized();

        var token = tokens.Issue(user);
        return Ok(new AuthResultDto(token.Token, token.ExpiresAtUtc, user.Id, user.Email!));
    }
}
