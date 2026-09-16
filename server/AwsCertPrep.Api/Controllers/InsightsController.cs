using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Insights;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Controllers;

[ApiController]
[Route("api/insights")]
[Produces("application/json")]
public class InsightsController(IMediator mediator) : ControllerBase
{
    private string UserKey => HttpContext.GetUserKey();

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
