using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Insights;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Controllers;

[ApiController]
[Route("api/insights")]
[Authorize]
[Produces("application/json")]
public class InsightsController(IMediator mediator) : ControllerBase
{
    /// <summary>The signed-in account. [Authorize] above guarantees there is one.</summary>
    private string UserKey => User.GetUserId();

    /// <summary>
    /// Readiness projection and per-domain recommendations. Rate limited: a cold call trains and
    /// cross-validates a model on the caller's answer history.
    /// </summary>
    [HttpGet("{certificationCode}/readiness")]
    [EnableRateLimiting(RateLimitPolicies.Insights)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ReadinessDto>> Readiness(string certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(new ReadinessQuery(certificationCode, UserKey), ct));
}
