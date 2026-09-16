using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Application.Lessons;

/// <summary>
/// Read-side helpers shared by the lesson handlers: resolving a topic, assembling a detail
/// response, and scoring a learner's mastery.
///
/// This is not the old service under a new name. It holds no request logic and makes no
/// decisions about what a request means - the handlers do that. It exists because three handlers
/// assemble the same detail response, and duplicating that assembly three times is how the
/// shapes drift apart.
/// </summary>
public class LessonReadModel(IUnitOfWork uow, LessonNoteWriter noteWriter, IOptions<LessonOptions> options)
{
    private readonly LessonOptions _options = options.Value;

    public bool AiConfigured => noteWriter.IsConfigured;

    public async Task<Certification> FindCertificationAsync(string code, CancellationToken ct) =>
        await uow.Repository<Certification>().Query().FirstOrDefaultAsync(c => c.Code == code, ct)
        ?? throw new KeyNotFoundException($"Unknown certification code '{code}'.");

    /// <summary>
    /// Resolves a certification and one of its topics in a single round trip. Tracked, because the
    /// write paths attach generated notes and view counts to what comes back.
    /// </summary>
    public async Task<(Certification Cert, LessonTopic Topic)> FindTopicAsync(
        string code, string slug, CancellationToken ct)
    {
        var topic = await uow.Repository<LessonTopic>()
            .Query(tracked: true)
            .Include(t => t.Domain)
            .Include(t => t.Content)
            .Include(t => t.Certification)
            .FirstOrDefaultAsync(t => t.Certification!.Code == code && t.Slug == slug, ct);

        if (topic is not null) return (topic.Certification!, topic);

        // Tell the two failures apart: an unknown code is a different mistake from a good code
        // with a slug that does not belong to it, and the client shows the message verbatim.
        _ = await FindCertificationAsync(code, ct);
        throw new KeyNotFoundException($"No lesson '{slug}' for {code}.");
    }

    public async Task<LessonDetailDto> BuildDetailAsync(
        Certification cert, LessonTopic topic, string userKey, string? warning, CancellationToken ct)
    {
        var mastery = await MasteryIndexAsync(cert.Id, userKey, ct);
        var completedIds = await CompletedTopicIdsAsync(cert.Id, userKey, ct);
        var practice = await PracticeQuestionsAsync(cert.Id, topic, ct);
        var related = await RelatedAsync(cert.Id, topic, ct);
        var (previous, next) = await NeighboursAsync(cert.Id, topic, ct);

        return new LessonDetailDto(
            topic.Slug,
            topic.Title,
            topic.Category,
            topic.Domain?.Name,
            topic.Domain?.WeightPercent,
            topic.Purpose,
            topic.PricingModel,
            topic.DocsUrl,
            topic.PricingUrl,
            mastery.For(topic),
            completedIds.Contains(topic.Id),
            topic.Content is null ? null : ToBody(topic.Content),
            noteWriter.IsConfigured,
            warning,
            practice,
            related,
            previous,
            next);
    }

    /// <summary>
    /// How well the learner does on each topic, from their answered exam items. One query,
    /// projected to the three columns the scoring needs: a topic's service tags are a
    /// comma-separated column, so matching them in SQL would mean a LIKE per topic per request.
    /// </summary>
    public async Task<MasteryIndex> MasteryIndexAsync(int certificationId, string userKey, CancellationToken ct)
    {
        var answered = await uow.Repository<ExamSessionQuestion>()
            .Query()
            .Where(i => i.ExamSession!.UserKey == userKey
                        && i.ExamSession.CertificationId == certificationId
                        && i.IsCorrect != null)
            .Select(i => new AnsweredItem(i.IsCorrect!.Value, i.Question!.ServiceTags, i.Question.DomainId))
            .ToListAsync(ct);

        return new MasteryIndex(answered, _options);
    }

    public async Task<HashSet<int>> CompletedTopicIdsAsync(
        int certificationId, string userKey, CancellationToken ct) =>
        (await uow.Repository<LessonProgress>()
            .Query()
            .Where(p => p.UserKey == userKey
                        && p.CompletedAt != null
                        && p.LessonTopic!.CertificationId == certificationId)
            .Select(p => p.LessonTopicId)
            .ToListAsync(ct))
        .ToHashSet();

    /// <summary>
    /// Questions from the learner's own bank that test this topic, so a lesson ends in practice
    /// rather than in prose. Prefers items tagged with one of the topic's service names; falls
    /// back to the topic's exam domain for concept topics no service name identifies.
    /// </summary>
    public async Task<IReadOnlyList<QuestionDto>> PracticeQuestionsAsync(
        int certificationId, LessonTopic topic, CancellationToken ct)
    {
        var questions = uow.Repository<Question>();
        var aliases = TopicTags.Split(topic.ServiceTags);

        List<Guid> candidateIds;

        if (aliases.Count > 0)
        {
            // Two queries rather than one: the first is a narrow id+tags projection used only to
            // find matches, and only the chosen questions are then loaded with their options.
            // Matching runs in memory because a SQL Contains on the comma-separated ServiceTags
            // column would let the "Amazon EC2" lesson claim questions tagged "Amazon EC2 Auto Scaling".
            var candidates = await questions
                .Query()
                .Where(q => q.CertificationId == certificationId
                            && q.RetiredReason == null
                            && q.ServiceTags != null)
                .Select(q => new { q.Id, q.ServiceTags })
                .ToListAsync(ct);

            candidateIds = candidates
                .Where(c => TopicTags.Matches(c.ServiceTags, aliases))
                .Select(c => c.Id)
                .ToList();
        }
        else if (topic.DomainId is not null)
        {
            candidateIds = await questions
                .Query()
                .Where(q => q.CertificationId == certificationId
                            && q.RetiredReason == null
                            && q.DomainId == topic.DomainId)
                .Select(q => q.Id)
                .ToListAsync(ct);
        }
        else
        {
            return [];
        }

        if (candidateIds.Count == 0) return [];

        var chosen = candidateIds
            .OrderBy(_ => Guid.NewGuid())
            .Take(_options.PracticeQuestionCount)
            .ToList();

        var found = await questions
            .Query()
            .Include(q => q.Domain)
            .Include(q => q.Options)
            .Where(q => chosen.Contains(q.Id))
            .ToListAsync(ct);

        return found.Select(q => QuestionMapper.ToDto(q, includeAnswers: true)).ToList();
    }

    public async Task<IReadOnlyList<LessonLinkDto>> RelatedAsync(
        int certificationId, LessonTopic topic, CancellationToken ct) =>
        await uow.Repository<LessonTopic>()
            .Query()
            .Where(t => t.CertificationId == certificationId
                        && t.Id != topic.Id
                        && t.Category == topic.Category)
            .OrderBy(t => t.Order)
            .Take(_options.RelatedTopicCount)
            .Select(t => new LessonLinkDto(t.Slug, t.Title))
            .ToListAsync(ct);

    /// <summary>
    /// The lesson before and after this one in curriculum order.
    ///
    /// Curriculum order, not the personalised order the list uses: that one is recomputed from the
    /// learner's answers and would make "next" mean something different on every visit, and could
    /// walk them in a loop. The seeded order runs domain by domain, so following it reads as a
    /// syllabus.
    /// </summary>
    public async Task<(LessonLinkDto? Previous, LessonLinkDto? Next)> NeighboursAsync(
        int certificationId, LessonTopic topic, CancellationToken ct)
    {
        var siblings = uow.Repository<LessonTopic>()
            .Query()
            .Where(t => t.CertificationId == certificationId && t.Id != topic.Id);

        var previous = await siblings
            .Where(t => t.Order < topic.Order)
            .OrderByDescending(t => t.Order)
            .Select(t => new LessonLinkDto(t.Slug, t.Title))
            .FirstOrDefaultAsync(ct);

        var next = await siblings
            .Where(t => t.Order > topic.Order)
            .OrderBy(t => t.Order)
            .Select(t => new LessonLinkDto(t.Slug, t.Title))
            .FirstOrDefaultAsync(ct);

        return (previous, next);
    }

    public static LessonBodyDto ToBody(LessonContent c) => new(
        c.Overview,
        Split(c.UseCases),
        c.CostNotes,
        Split(c.Integrations),
        c.RealWorldExample,
        Split(c.ExamTraps),
        c.Provider,
        c.Model,
        c.GeneratedAt);

    public static IReadOnlyList<string> Split(string value) =>
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// What to study next. Weak topics first, then ones in progress, then untested topics the exam
    /// leans on, then the rest, and finally the ones already mastered.
    /// </summary>
    public static int Priority(MasteryStatus status, bool isCore) => status switch
    {
        MasteryStatus.Weak => 0,
        MasteryStatus.Learning => 1,
        MasteryStatus.Untested when isCore => 2,
        MasteryStatus.Untested => 3,
        _ => 4,
    };
}

public record AnsweredItem(bool IsCorrect, string? ServiceTags, int? DomainId);

/// <summary>
/// Matches a question's service tags against a topic's aliases.
///
/// Both sides are comma-separated free text: the bank holds "Amazon EBS" on one item and
/// "AWS Key Management Service (AWS KMS)" on another, because tags come from what the question
/// says. Whole tags are compared rather than substrings - a substring test would let the
/// "Amazon EC2" lesson claim every question tagged "Amazon EC2 Auto Scaling" - and the
/// parenthetical that generated tags often append is dropped before comparing.
/// </summary>
public static class TopicTags
{
    public static List<string> Split(string? value) =>
        (value ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalise)
            .Where(t => t.Length > 0)
            .ToList();

    public static bool Matches(string? questionTags, List<string> topicAliases) =>
        Split(questionTags).Any(topicAliases.Contains);

    private static string Normalise(string tag)
    {
        var open = tag.IndexOf('(');
        if (open > 0) tag = tag[..open];
        return tag.Trim().ToLowerInvariant();
    }
}

/// <summary>The learner's answered items, ready to be scored against any topic.</summary>
public class MasteryIndex(List<AnsweredItem> answered, LessonOptions options)
{
    public LessonMasteryDto For(LessonTopic topic)
    {
        var aliases = TopicTags.Split(topic.ServiceTags);
        var byTopic = aliases.Count > 0;

        var matches = byTopic
            ? answered.Where(a => TopicTags.Matches(a.ServiceTags, aliases)).ToList()
            : answered.Where(a => topic.DomainId != null && a.DomainId == topic.DomainId).ToList();

        var basis = byTopic ? MasteryBasis.Topic : MasteryBasis.Domain;

        var total = matches.Count;
        var correct = matches.Count(a => a.IsCorrect);

        if (total < options.MinAnswersForMastery)
            return new LessonMasteryDto(total, correct, null, MasteryStatus.Untested, basis);

        var accuracy = Math.Round(100m * correct / total, 1);

        var status = accuracy < options.WeakThresholdPercent ? MasteryStatus.Weak
            : accuracy < options.StrongThresholdPercent ? MasteryStatus.Learning
            : MasteryStatus.Strong;

        return new LessonMasteryDto(total, correct, accuracy, status, basis);
    }
}
