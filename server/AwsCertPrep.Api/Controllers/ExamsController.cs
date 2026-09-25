using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Exams;
using AwsCertPrep.Api.Application.Insights;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AwsCertPrep.Api.Controllers;

/// <summary>
/// Exam sessions. A session is a resource: POST creates one, GET reads it, and answers and the
/// submission are sub-resources of it rather than verbs hung off the collection.
/// </summary>
[ApiController]
[Route("api/exams")]
[Authorize]
[Produces("application/json")]
public class ExamsController(IMediator mediator) : ControllerBase
{
    /// <summary>The signed-in account. [Authorize] above guarantees there is one.</summary>
    private string UserKey => User.GetUserId();

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExamSessionDto>> Start(
        [FromBody] StartExamRequest request, CancellationToken ct)
    {
        var session = await mediator.SendAsync(
            new StartExamCommand(
                request.CertificationCode,
                request.Mode,
                request.QuestionCount,
                request.DomainId,
                request.Difficulty,
                UserKey),
            ct);

        // 201 with a Location header: the client gets the canonical URL of what it just made.
        return CreatedAtAction(nameof(Get), new { id = session.Id }, session);
    }

    /// <summary>
    /// How many questions this learner has waiting in review for a certification.
    /// Declared before the {id:guid} route so the literal segment is matched first.
    /// </summary>
    [HttpGet("review-count")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ReviewCountDto>> ReviewCount(
        [FromQuery] string certificationCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(certificationCode))
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Missing parameter",
                Detail = "certificationCode is required.",
                Instance = HttpContext.Request.Path,
            });

        return Ok(await mediator.SendAsync(new ReviewCountQuery(certificationCode, UserKey), ct));
    }

    [HttpGet("history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExamResultDto>>> History(
        [FromQuery] string? certificationCode, CancellationToken ct) =>
        Ok(await mediator.SendAsync(new ExamHistoryQuery(UserKey, certificationCode), ct));

    [HttpGet("{id:guid}", Name = nameof(Get))]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExamSessionDto>> Get(Guid id, CancellationToken ct)
    {
        var session = await mediator.SendAsync(new GetExamQuery(id, UserKey), ct);

        return session is null
            ? NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Exam session not found",
                Detail = "No exam session with that id belongs to you.",
                Instance = HttpContext.Request.Path,
            })
            : Ok(session);
    }

    [HttpPost("{id:guid}/answers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnswerResponse>> Answer(
        Guid id, [FromBody] AnswerRequest request, CancellationToken ct) =>
        Ok(await mediator.SendAsync(
            new AnswerExamQuestionCommand(
                id, request.QuestionId, request.SelectedLabels, request.SecondsSpent, UserKey),
            ct));

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExamResultDto>> Submit(Guid id, CancellationToken ct) =>
        Ok(await mediator.SendAsync(new SubmitExamCommand(id, UserKey), ct));
}
