using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Application.Lessons;

/// <summary>
/// Generates the notes for a lesson and returns the whole lesson with them attached.
/// <paramref name="ReplaceExisting"/> false is the first-open case and is a no-op when notes
/// already exist, so a double-click cannot spend two provider calls on the same topic.
/// </summary>
public record WriteLessonNotesCommand(
    [property: Required] string CertificationCode,
    [property: Required] string Slug,
    [property: Required] string UserKey,
    bool ReplaceExisting) : ICommand<LessonDetailDto>;

public class WriteLessonNotesHandler(
    LessonReadModel read,
    LessonNoteWriter noteWriter,
    ILogger<WriteLessonNotesHandler> logger)
    : ICommandHandler<WriteLessonNotesCommand, LessonDetailDto>
{
    public async Task<LessonDetailDto> HandleAsync(WriteLessonNotesCommand request, CancellationToken ct)
    {
        var (cert, topic) = await read.FindTopicAsync(request.CertificationCode, request.Slug, ct);

        string? warning = null;

        if (topic.Content is null || request.ReplaceExisting)
        {
            try
            {
                topic.Content = await noteWriter.WriteAsync(topic, cert, ct);
            }
            catch (Exception ex) when (ex is AiProviderException or AiConfigurationException)
            {
                // The seeded half of the lesson is still worth showing, so a provider outage
                // degrades the page rather than replacing it with an error screen.
                logger.LogWarning(ex, "Lesson generation failed for {Slug}.", request.Slug);
                warning = $"The detailed notes could not be written right now ({ex.Message}) "
                          + "- the verified facts are unaffected.";
            }
        }

        return await read.BuildDetailAsync(cert, topic, request.UserKey, warning, ct);
    }
}

/// <summary>Marks a lesson complete for one learner, or clears that mark.</summary>
public record SetLessonProgressCommand(
    [property: Required] string CertificationCode,
    [property: Required] string Slug,
    [property: Required] string UserKey,
    bool Completed) : ICommand<LessonProgressDto>;

public class SetLessonProgressHandler(IUnitOfWork uow, LessonReadModel read)
    : ICommandHandler<SetLessonProgressCommand, LessonProgressDto>
{
    public async Task<LessonProgressDto> HandleAsync(SetLessonProgressCommand request, CancellationToken ct)
    {
        var (_, topic) = await read.FindTopicAsync(request.CertificationCode, request.Slug, ct);

        var progress = uow.Repository<LessonProgress>();

        var row = await progress
            .Query(tracked: true)
            .FirstOrDefaultAsync(p => p.UserKey == request.UserKey && p.LessonTopicId == topic.Id, ct);

        if (row is null)
        {
            row = new LessonProgress { LessonTopicId = topic.Id, UserKey = request.UserKey, ViewCount = 1 };
            progress.Add(row);
        }

        row.CompletedAt = request.Completed ? DateTime.UtcNow : null;
        await uow.SaveChangesAsync(ct);

        return new LessonProgressDto(topic.Slug, row.CompletedAt is not null, row.CompletedAt);
    }
}

/// <summary>
/// Answers a question about one lesson, with that lesson's facts and notes already in the prompt.
///
/// A command rather than a query despite persisting nothing: it costs a provider call and is not
/// safely repeatable, which is what the distinction is for here.
/// </summary>
public record AskTutorCommand(
    [property: Required] string CertificationCode,
    [property: Required] string Slug,
    [property: Required, MaxLength(500)] string Question,
    IReadOnlyList<TutorTurnDto>? History) : ICommand<TutorAnswerDto>;

public class AskTutorHandler(
    LessonReadModel read,
    LessonNoteWriter noteWriter,
    IOptions<TutorOptions> options,
    ILogger<AskTutorHandler> logger)
    : ICommandHandler<AskTutorCommand, TutorAnswerDto>
{
    private readonly TutorOptions _options = options.Value;

    public async Task<TutorAnswerDto> HandleAsync(AskTutorCommand request, CancellationToken ct)
    {
        var (cert, topic) = await read.FindTopicAsync(request.CertificationCode, request.Slug, ct);

        var question = request.Question.Trim();
        if (question.Length == 0)
            throw new InvalidOperationException("Ask a question first.");

        if (!noteWriter.IsConfigured)
            throw new AiConfigurationException(
                "No AI provider is configured, so the tutor is unavailable. Set Ai:Provider and Ai:ApiKey.");

        var history = (request.History ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t.Question) && !string.IsNullOrWhiteSpace(t.Answer))
            .TakeLast(_options.MaxHistoryTurns)
            .Select(t => new TutorTurn(t.Question, t.Answer))
            .ToList();

        var prompt = LessonTutorPrompt.Build(topic, cert, history, question, _options);
        var raw = await noteWriter.CompleteAsync(LessonTutorPrompt.SystemPrompt, prompt, ct);
        var parsed = TutorJsonParser.Parse(raw);

        logger.LogInformation("Tutor answered a question on {Slug}.", request.Slug);

        return new TutorAnswerDto(
            topic.Slug,
            topic.Title,
            parsed.Answer.Trim(),
            noteWriter.Provider,
            noteWriter.Model);
    }
}
