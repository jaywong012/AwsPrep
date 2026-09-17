using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Application.Questions;

/// <summary>Generates practice questions and stores the ones that survive validation.</summary>
public record GenerateQuestionsCommand(
    [property: Required] string CertificationCode,
    int? DomainId,
    Difficulty Difficulty,
    [property: Range(1, 20)] int Count,
    [property: MaxLength(300)] string? TopicHint) : ICommand<GenerateQuestionsResponse>;

public class GenerateQuestionsHandler(
    IUnitOfWork uow,
    IQuestionGenerator generator,
    ReferenceBank referenceBank,
    IOptions<QuestionOptions> options,
    ILogger<GenerateQuestionsHandler> logger)
    : ICommandHandler<GenerateQuestionsCommand, GenerateQuestionsResponse>
{
    private readonly QuestionOptions _options = options.Value;

    public async Task<GenerateQuestionsResponse> HandleAsync(
        GenerateQuestionsCommand request, CancellationToken ct)
    {
        var questions = uow.Repository<Question>();

        var cert = await uow.Repository<Certification>()
            .Query(tracked: true)
            .Include(c => c.Domains)
            .FirstOrDefaultAsync(c => c.Code == request.CertificationCode, ct)
            ?? throw new KeyNotFoundException($"Unknown certification code '{request.CertificationCode}'.");

        var targetDomain = request.DomainId is null
            ? null
            : cert.Domains.FirstOrDefault(d => d.Id == request.DomainId)
              ?? throw new KeyNotFoundException($"Domain {request.DomainId} does not belong to {cert.Code}.");

        // Questions in the domain being generated for matter most here: those are the ones a new
        // item is likely to duplicate, and the list is capped before it reaches the prompt.
        var targetDomainId = targetDomain?.Id ?? 0;
        var existingStems = await questions
            .Query()
            .Where(q => q.CertificationId == cert.Id)
            .OrderByDescending(q => q.DomainId == targetDomainId)
            .ThenByDescending(q => q.CreatedAt)
            .Select(q => q.Stem)
            .Take(_options.ExistingStemSampleSize)
            .ToListAsync(ct);

        var exemplars = referenceBank.Exemplars(cert.Code, targetDomain?.Name, _options.ExemplarCount);
        var undercoveredTopics = await UndercoveredTopicsAsync(cert, ct);

        var context = new GenerationContext(
            cert.Code,
            cert.Name,
            cert.Level,
            cert.TargetCandidate,
            cert.OutOfScopeTasks.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            cert.Domains.Select(d => d.Name).ToArray(),
            targetDomain?.Name,
            request.Difficulty,
            request.Count,
            request.TopicHint,
            existingStems,
            exemplars,
            undercoveredTopics);

        var result = await generator.GenerateAsync(context, ct);

        var created = new List<Question>();
        var rejectionReasons = new List<string>();
        var duplicates = 0;
        var rejected = 0;

        // Hashes already persisted plus hashes added in this batch, so a model repeating itself
        // inside one response cannot violate the unique index.
        var stored = await questions
            .Query()
            .Where(q => q.CertificationId == cert.Id)
            .Select(q => new { q.StemHash, q.Stem })
            .ToListAsync(ct);

        var seenHashes = stored.Select(q => q.StemHash).ToHashSet();

        // The hash only catches an exact repeat. A model given the same blueprint twice tends to
        // reword instead, so the stems are also compared on their meaningful words.
        var seenFingerprints = stored.Select(q => StemSimilarity.Fingerprint(q.Stem)).ToList();

        foreach (var generated in result.Questions)
        {
            if (!GeneratedQuestionValidator.IsValid(generated, cert.Level, out var reason))
            {
                logger.LogInformation("Rejected generated question ({Reason})", reason);
                rejectionReasons.Add(reason);
                rejected++;
                continue;
            }

            var hash = QuestionHasher.Hash(generated.Stem);
            if (!seenHashes.Add(hash))
            {
                duplicates++;
                continue;
            }

            if (StemSimilarity.IsRewordingOfAny(generated.Stem, seenFingerprints))
            {
                logger.LogInformation("Rejected a generated question as a rewording of one already in the bank.");
                duplicates++;
                continue;
            }

            seenFingerprints.Add(StemSimilarity.Fingerprint(generated.Stem));

            var domain = ResolveDomain(cert, targetDomain, generated.Domain);
            var correctCount = generated.Options.Count(o => o.IsCorrect);

            var question = new Question
            {
                CertificationId = cert.Id,
                DomainId = domain?.Id,
                Domain = domain,
                Stem = generated.Stem.Trim(),
                Type = correctCount > 1 ? QuestionType.MultipleChoice : QuestionType.SingleChoice,
                Difficulty = ParseDifficulty(generated.Difficulty) ?? request.Difficulty,
                Source = generator.Provider == "Offline" ? QuestionSource.Seed : QuestionSource.Ai,
                Explanation = Trim(generated.Explanation, 4000),
                ServiceTags = generated.ServiceTags.Count == 0
                    ? null
                    : Trim(string.Join(", ", generated.ServiceTags), 400),
                Model = Trim(result.Model, 100),
                StemHash = hash,
                Options = generated.Options
                    .Select((o, i) => new QuestionOption
                    {
                        Label = string.IsNullOrWhiteSpace(o.Label)
                            ? ((char)('A' + i)).ToString()
                            : o.Label.Trim().ToUpperInvariant(),
                        Text = Trim(o.Text, 1000),
                        IsCorrect = o.IsCorrect,
                    })
                    .ToList(),
            };

            questions.Add(question);
            created.Add(question);
        }

        // One commit for the whole batch: a half-saved batch would leave the bank in a state the
        // caller was never told about.
        if (created.Count > 0) await uow.SaveChangesAsync(ct);

        return new GenerateQuestionsResponse(
            result.Provider,
            result.Model,
            request.Count,
            created.Count,
            duplicates,
            rejected,
            rejectionReasons.Distinct().Take(5).ToList(),
            created.Select(q => QuestionMapper.ToDto(q, includeAnswers: true)).ToList(),
            result.Warning);
    }

    /// <summary>
    /// Topics the reference bank shows the exam actually tests, ranked by how often it tests them,
    /// minus the ones this bank already covers. Feeding these back into the prompt is what stops
    /// repeated generation runs circling the same handful of headline services.
    /// </summary>
    private async Task<IReadOnlyList<string>> UndercoveredTopicsAsync(Certification cert, CancellationToken ct)
    {
        var examTopics = referenceBank.Topics(cert.Code);
        if (examTopics.Count == 0) return [];

        var taggedRows = await uow.Repository<Question>()
            .Query()
            .Where(q => q.CertificationId == cert.Id && q.RetiredReason == null && q.ServiceTags != null)
            .Select(q => q.ServiceTags!)
            .ToListAsync(ct);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in taggedRows.SelectMany(row =>
                     row.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            counts[tag] = counts.GetValueOrDefault(tag) + 1;
        }

        return examTopics
            .Where(topic => counts.GetValueOrDefault(topic) < _options.CoveredThreshold)
            .Take(_options.UndercoveredTopicCount)
            .ToList();
    }

    private static CertificationDomain? ResolveDomain(
        Certification cert, CertificationDomain? forced, string? name)
    {
        if (forced is not null) return forced;
        if (string.IsNullOrWhiteSpace(name)) return null;

        return cert.Domains.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? cert.Domains.FirstOrDefault(d => d.Name.Contains(name, StringComparison.OrdinalIgnoreCase)
                                                   || name.Contains(d.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static Difficulty? ParseDifficulty(string? value) =>
        Enum.TryParse<Difficulty>(value, ignoreCase: true, out var d) ? d : null;

    private static string Trim(string? value, int max)
    {
        var v = (value ?? "").Trim();
        return v.Length <= max ? v : v[..max];
    }
}

/// <summary>Deletes one question that no exam session references.</summary>
public record DeleteQuestionCommand(Guid Id) : ICommand<bool>;

public class DeleteQuestionHandler(IUnitOfWork uow) : ICommandHandler<DeleteQuestionCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteQuestionCommand request, CancellationToken ct)
    {
        var questions = uow.Repository<Question>();

        var question = await questions
            .Query(tracked: true)
            .FirstOrDefaultAsync(q => q.Id == request.Id, ct);

        if (question is null) return false;

        var inUse = await uow.Repository<ExamSessionQuestion>()
            .Query()
            .AnyAsync(x => x.QuestionId == request.Id, ct);

        if (inUse)
            throw new InvalidOperationException(
                "This question is referenced by an exam session and cannot be deleted.");

        questions.Remove(question);
        await uow.SaveChangesAsync(ct);
        return true;
    }
}
