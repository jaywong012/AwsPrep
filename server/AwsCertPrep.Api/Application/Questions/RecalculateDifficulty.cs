using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Application.Questions;

/// <summary>
/// Re-rates every question's difficulty from the shape of the item.
///
/// <paramref name="Apply"/> false is a dry run that reports what would change and writes nothing,
/// because this rewrites a column across the whole bank and the effect should be visible before
/// it happens. The rating is recomputed from the item itself, so running it again is harmless and
/// the change is always reversible by running it again after adjusting the rater.
/// </summary>
public record RecalculateDifficultyCommand(string? CertificationCode, bool Apply)
    : ICommand<DifficultyRecalculationDto>;

public class RecalculateDifficultyHandler(IUnitOfWork uow, ILogger<RecalculateDifficultyHandler> logger)
    : ICommandHandler<RecalculateDifficultyCommand, DifficultyRecalculationDto>
{
    public async Task<DifficultyRecalculationDto> HandleAsync(
        RecalculateDifficultyCommand request, CancellationToken ct)
    {
        var repository = uow.Repository<Question>();

        // Tracked only when applying: a dry run has no reason to hold the whole bank in the
        // change tracker.
        var questions = await repository
            .Query(tracked: request.Apply)
            .Include(q => q.Options)
            .Where(q => request.CertificationCode == null
                        || q.Certification!.Code == request.CertificationCode)
            .ToListAsync(ct);

        var before = Tally(questions.Select(q => q.Difficulty));
        var changes = new List<DifficultyChangeDto>();
        var rated = new List<Difficulty>();

        foreach (var question in questions)
        {
            var correctCount = question.Options.Count(o => o.IsCorrect);
            var next = QuestionDifficultyRater.Rate(question.Stem, correctCount);
            rated.Add(next);

            if (next == question.Difficulty) continue;

            changes.Add(new DifficultyChangeDto(
                question.Id,
                question.Stem.Length <= 90 ? question.Stem : question.Stem[..90] + "...",
                question.Difficulty,
                next));

            if (request.Apply) question.Difficulty = next;
        }

        if (request.Apply && changes.Count > 0)
        {
            await uow.SaveChangesAsync(ct);
            logger.LogInformation(
                "Re-rated difficulty on {Count} questions for {Code}.",
                changes.Count, request.CertificationCode ?? "all certifications");
        }

        return new DifficultyRecalculationDto(
            request.CertificationCode,
            request.Apply,
            questions.Count,
            changes.Count,
            before,
            Tally(rated),
            // A sample rather than the lot: the point is to show what the change looks like, and
            // several hundred rows in a response body helps nobody.
            changes.Take(20).ToList());
    }

    private static DifficultyTallyDto Tally(IEnumerable<Difficulty> values)
    {
        var list = values.ToList();
        return new DifficultyTallyDto(
            list.Count(d => d == Difficulty.Easy),
            list.Count(d => d == Difficulty.Medium),
            list.Count(d => d == Difficulty.Hard));
    }
}
