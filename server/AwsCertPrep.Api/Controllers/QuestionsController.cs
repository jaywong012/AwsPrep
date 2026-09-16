using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Questions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Controllers;

/// <summary>
/// The question bank.
///
/// <c>/audit</c> is a sub-resource of the collection rather than a verb on it: GET reports what is
/// wrong and POST to <c>/audit/clean</c> acts on that report, which keeps the read safe to repeat
/// and the write explicit.
/// </summary>
[ApiController]
[Route("api/questions")]
[Produces("application/json")]
public class QuestionsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Generates practice questions and stores them. Rate limited: each call is a paid or
    /// quota-limited call to an LLM provider.
    /// </summary>
    [HttpPost("generate")]
    [EnableRateLimiting(RateLimitPolicies.Generate)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GenerateQuestionsResponse>> Generate(
        [FromBody] GenerateQuestionsRequest request, CancellationToken ct) =>
        Ok(await mediator.SendAsync(
            new GenerateQuestionsCommand(
                request.CertificationCode,
                request.DomainId,
                request.Difficulty,
                request.Count,
                request.TopicHint),
            ct));

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<QuestionDto>>> Browse(
        [FromQuery] string certificationCode,
        [FromQuery] int? domainId,
        [FromQuery] Difficulty? difficulty,
        [FromQuery] bool includeRetired = false,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(certificationCode))
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing parameter",
                Detail = "certificationCode is required.",
                Instance = HttpContext.Request.Path,
            });

        return Ok(await mediator.SendAsync(
            new BrowseQuestionsQuery(
                certificationCode,
                domainId,
                difficulty,
                includeRetired,
                Math.Max(0, skip),
                Math.Clamp(take, 1, 100)),
            ct));
    }

    /// <summary>Deletes one unused question. Destructive and shared, so it needs the admin key.</summary>
    [HttpDelete("{id:guid}")]
    [ServiceFilter(typeof(AdminOnlyFilter))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        await mediator.SendAsync(new DeleteQuestionCommand(id), ct) ? NoContent() : NotFound();

    /// <summary>
    /// Dry run: reports stored questions that break the official item format or fall outside the
    /// certification's scope. Nothing is modified.
    /// </summary>
    [HttpGet("audit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditReportDto>> Audit(
        [FromQuery] string? certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(new AuditQuestionsQuery(certificationCode), ct));

    /// <summary>
    /// Re-rates every question's difficulty from the shape of the item, so the label means the
    /// same thing whether a question came from the reference bank or a generator.
    ///
    /// GET is the dry run and writes nothing; POST applies it. Admin-gated because it rewrites a
    /// column across the shared bank.
    /// </summary>
    [HttpGet("difficulty")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DifficultyRecalculationDto>> PreviewDifficulty(
        [FromQuery] string? certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(
            new RecalculateDifficultyCommand(certificationCode, Apply: false), ct));

    [HttpPost("difficulty")]
    [ServiceFilter(typeof(AdminOnlyFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DifficultyRecalculationDto>> RecalculateDifficulty(
        [FromQuery] string? certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(
            new RecalculateDifficultyCommand(certificationCode, Apply: true), ct));

    /// <summary>
    /// Applies the audit: deletes flagged questions nobody has answered and retires the ones
    /// referenced by exam history, so past results stay explainable.
    /// </summary>
    [HttpPost("audit/clean")]
    [ServiceFilter(typeof(AdminOnlyFilter))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditCleanupDto>> Clean(
        [FromQuery] string? certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(new CleanAuditedQuestionsCommand(certificationCode), ct));
}
