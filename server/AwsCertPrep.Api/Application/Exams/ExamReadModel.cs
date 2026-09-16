using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Application.Exams;

/// <summary>
/// Loading and shaping shared by the exam handlers. No request logic lives here: it exists so the
/// ownership rule below is written once, and so the session-to-DTO mapping cannot drift between
/// the start, get and submit paths.
/// </summary>
public class ExamReadModel(IUnitOfWork uow, IOptions<ExamOptions> options)
{
    private readonly ExamOptions _options = options.Value;

    /// <summary>
    /// Sessions are always loaded by id AND owner: an exam id is a bearer token in this app, so
    /// another learner presenting a valid id must get the same answer as one presenting a made-up
    /// id - not found.
    /// </summary>
    public Task<ExamSession?> LoadAsync(Guid id, string userKey, bool tracked, CancellationToken ct) =>
        uow.Repository<ExamSession>()
            .Query(tracked)
            .Include(s => s.Certification)
            .Include(s => s.Items.OrderBy(i => i.Order))
                .ThenInclude(i => i.Question)
                    .ThenInclude(q => q!.Options)
            .Include(s => s.Items)
                .ThenInclude(i => i.Question)
                    .ThenInclude(q => q!.Domain)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserKey == userKey, ct);

    /// <summary>
    /// True once a timed exam is past its deadline. Practice, review and untimed sessions
    /// (DurationMinutes = 0) never expire.
    /// </summary>
    public bool HasExpired(ExamSession session) =>
        session.Mode == ExamMode.Exam
        && session.DurationMinutes > 0
        && DateTime.UtcNow > session.StartedAt
            .AddMinutes(session.DurationMinutes)
            .AddSeconds(_options.DeadlineGraceSeconds);

    /// <summary>
    /// Questions whose most recent answer from this learner was wrong.
    ///
    /// Most-recent rather than ever-wrong on purpose: once you get something right it leaves the
    /// queue, and if you miss it again later it comes back. Reduced in memory because it is one
    /// learner's own answers, and because "the latest row per question" is awkward to express in
    /// LINQ without the provider pulling the rows down anyway.
    /// </summary>
    public async Task<List<Guid>> QuestionsToReviewAsync(
        int certificationId, string userKey, CancellationToken ct)
    {
        var answers = await uow.Repository<ExamSessionQuestion>()
            .Query()
            .Where(i => i.ExamSession!.UserKey == userKey
                        && i.ExamSession.CertificationId == certificationId
                        && i.IsCorrect != null
                        && i.AnsweredAt != null)
            .Select(i => new { i.QuestionId, i.IsCorrect, i.AnsweredAt })
            .ToListAsync(ct);

        return answers
            .GroupBy(a => a.QuestionId)
            .Where(g => g.OrderByDescending(a => a.AnsweredAt).First().IsCorrect == false)
            .Select(g => g.Key)
            .ToList();
    }

    /// <summary>
    /// Estimates the official 100-1000 scaled score. AWS does not publish its raw-to-scaled
    /// mapping (it varies per exam form), so this is a linear estimate and is labelled as such in
    /// the UI. 700 on this scale corresponds to about 67% of scored items correct.
    /// </summary>
    public static int ScaledScore(decimal percentCorrect) =>
        (int)Math.Round(100 + 9m * Math.Clamp(percentCorrect, 0m, 100m));

    public static List<DomainScoreDto> Breakdown(IEnumerable<ExamSessionQuestion> scoredItems) =>
        scoredItems
            .GroupBy(i => i.Question!.Domain?.Name ?? "Unclassified")
            .Select(g =>
            {
                var total = g.Count();
                var correct = g.Count(x => x.IsCorrect == true);
                return new DomainScoreDto(
                    g.Key, correct, total, total == 0 ? 0m : Math.Round(correct * 100m / total, 1));
            })
            .OrderBy(d => d.Domain)
            .ToList();

    public static ExamSessionDto ToDto(ExamSession session, Certification cert, bool revealAnswers) => new(
        session.Id,
        cert.Code,
        cert.Name,
        session.Mode,
        session.DurationMinutes,
        cert.PassingScore,
        session.StartedAt,
        session.CompletedAt,
        session.ScorePercent,
        session.Passed,
        session.Items
            .OrderBy(i => i.Order)
            .Select(i => new ExamItemDto(
                i.Order,
                i.QuestionId,
                QuestionMapper.ToDto(i.Question!, revealAnswers),
                i.SelectedLabels,
                revealAnswers ? i.IsCorrect : null))
            .ToList());

    public static ExamResultDto ToResult(ExamSession session, Certification cert)
    {
        var total = session.Items.Count;
        var unanswered = session.Items.Count(i => i.AnsweredAt is null);

        // Only scored items count, exactly as on the real exam.
        var scoredItems = session.Items.Where(i => i.IsScored).ToList();
        var correct = scoredItems.Count(i => i.IsCorrect == true);
        var score = session.ScorePercent
                    ?? (scoredItems.Count == 0 ? 0m : Math.Round(correct * 100m / scoredItems.Count, 2));
        var scaled = session.ScaledScore ?? ScaledScore(score);

        return new ExamResultDto(
            session.Id,
            cert.Code,
            score,
            scaled,
            cert.PassingScaledScore,
            session.Passed ?? scaled >= cert.PassingScaledScore,
            cert.PassingScore,
            correct,
            scoredItems.Count,
            total,
            unanswered,
            session.Items.Sum(i => i.SecondsSpent),
            Breakdown(scoredItems));
    }
}
