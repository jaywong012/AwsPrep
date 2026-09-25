using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Certifications;
using AwsCertPrep.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AwsCertPrep.Api.Controllers;

/// <summary>
/// Certifications are a read-only collection here: they are seeded from the official exam guides,
/// so the resource exposes a list and an item and nothing else.
/// </summary>
[ApiController]
[Route("api/certifications")]
// The seeded blueprint catalogue: shared, read-only, and needed before anyone can sign in.
[AllowAnonymous]
public class CertificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CertificationDto>>> GetAll(CancellationToken ct) =>
        Ok(await mediator.SendAsync(new ListCertificationsQuery(), ct));

    [HttpGet("{code}", Name = nameof(GetCertification))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificationDto>> GetCertification(string code, CancellationToken ct)
    {
        var match = (await mediator.SendAsync(new ListCertificationsQuery(code), ct)).FirstOrDefault();

        return match is null
            ? NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Certification not found",
                Detail = $"No certification with code '{code}'.",
                Instance = HttpContext.Request.Path,
            })
            : Ok(match);
    }
}
