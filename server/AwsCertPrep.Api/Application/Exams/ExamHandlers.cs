using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Application.Exams;

// ---------- commands ----------

/// <summary>Starts a practice set, a timed mock, or a review of what this learner got wrong.</summary>
public record StartExamCommand(
    [property: Required] string CertificationCode,
    ExamMode Mode,
    int? QuestionCount,
    int? DomainId,
    Difficulty? Difficulty,
    [property: Required] string UserKey) : ICommand<ExamSessionDto>;

public class StartExamHandler(IUnitOfWork uow, ExamReadModel read)
    : ICommandHandler<StartExamCommand, ExamSessionDto>
{
    public async Task<ExamSessionDto> HandleAsync(StartExamCommand request, CancellationToken ct)
    {
        var cert = await uow.Repository<Certification>()
            .Query(tracked: true)
            .Include(c => c.Domains)
            .FirstOrDefaultAsync(c => c.Code == request.CertificationCode, ct)
            ?? throw new KeyNotFoundException($"Unknown certification code '{request.CertificationCode}'.");

        var pool = uow.Repository<Question>()
            .Query(tracked: true)
            .Include(q => q.Options)
            .Include(q => q.Domain)
            .Where(q => q.CertificationId == cert.Id && q.RetiredReason == null);

        if (request.DomainId is not null) pool = pool.Where(q => q.DomainId == request.DomainId);
        if (request.Difficulty is not null) pool = pool.Where(q => q.Difficulty == request.Difficulty);

        if (request.Mode == ExamMode.Review)
        {
            var toReview = await read.QuestionsToReviewAsync(cert.Id, request.UserKey, ct);

            if (toReview.Count == 0)
                throw new InvalidOperationException(
                    "Nothing to review yet - you have not answered any question incorrectly for this "
                    + "certification. Sit a practice set first, and anything you miss will collect here.");

            pool = pool.Where(q => toReview.Contains(q.Id));
        }

        var requested = request.QuestionCount ?? cert.ExamQuestionCount;

        // Random selection happens in SQL (Guid.NewGuid translates to NEWID), so the whole bank is
        // never pulled into memory to pick a handful of questions.
        var questions = await pool
            .OrderBy(_ => Guid.NewGuid())
            .Take(requested)
            .ToListAsync(ct);

        if (questions.Count == 0)
            throw new InvalidOperationException(
                $"No questions available for {cert.Code} with the chosen filters. Generate some first.");

        var minutes = request.Mode == ExamMode.Exam
            ? Math.Max(1, (int)Math.Round(
                cert.DurationMinutes * (double)questions.Count / cert.ExamQuestionCount))
            : 0;

        // A mock exam mirrors the real 50-scored-of-65 split: a proportional slice is marked
        // unscored and excluded from the score, without being identified to the candidate.
        // Practice and review score everything, since their purpose is feedback.
        var unscoredCount = request.Mode == ExamMode.Exam && cert.UnscoredQuestionCount > 0
            ? (int)Math.Round(questions.Count * (double)cert.UnscoredQuestionCount / cert.ExamQuestionCount)
            : 0;
        unscoredCount = Math.Min(unscoredCount, Math.Max(0, questions.Count - 1));

        var unscoredIds = questions
            .OrderBy(_ => Guid.NewGuid())
            .Take(unscoredCount)
            .Select(q => q.Id)
            .ToHashSet();

        var session = new ExamSession
        {
            CertificationId = cert.Id,
            Certification = cert,
            UserKey = request.UserKey,
            Mode = request.Mode,
            DurationMinutes = minutes,
            Items = questions
                .Select((q, i) => new ExamSessionQuestion
                {
                    QuestionId = q.Id,
                    Question = q,
                    Order = i + 1,
                    IsScored = !unscoredIds.Contains(q.Id),
                })
                .ToList(),
        };

        uow.Repository<ExamSession>().Add(session);
        await uow.SaveChangesAsync(ct);

        return ExamReadModel.ToDto(session, cert, revealAnswers: false);
    }
}

/// <summary>Records one answer and, outside a timed mock, says immediately whether it was right.</summary>
public record AnswerExamQuestionCommand(
    Guid SessionId,
    Guid QuestionId,
    IReadOnlyList<string> SelectedLabels,
    int SecondsSpent,
    [property: Required] string UserKey) : ICommand<AnswerResponse>;

public class AnswerExamQuestionHandler(IUnitOfWork uow, ExamReadModel read)
    : ICommandHandler<AnswerExamQuestionCommand, AnswerResponse>
{
    public async Task<AnswerResponse> HandleAsync(AnswerExamQuestionCommand request, CancellationToken ct)
    {
        var session = await read.LoadAsync(request.SessionId, request.UserKey, tracked: true, ct)
            ?? throw new KeyNotFoundException("Exam session not found.");

        if (session.CompletedAt is not null)
            throw new InvalidOperationException("This exam session is already submitted.");

        // A timed exam is timed on the server too. Without this the countdown is advisory: a
        // client could keep answering long after it hit zero, or simply never run the timer.
        if (read.HasExpired(session))
            throw new InvalidOperationException("Time is up for this exam. Submit it to see your score.");

        var item = session.Items.FirstOrDefault(i => i.QuestionId == request.QuestionId)
            ?? throw new KeyNotFoundException("That question is not part of this exam session.");

        var selected = request.SelectedLabels
            .Select(l => l.Trim().ToUpperInvariant())
            .Where(l => l.Length > 0)
            .Distinct()
            .OrderBy(l => l)
            .ToArray();

        var correctLabels = item.Question!.Options
            .Where(o => o.IsCorrect)
            .Select(o => o.Label)
            .OrderBy(l => l)
            .ToArray();

        var isCorrect = selected.Length > 0 && selected.SequenceEqual(correctLabels);

        item.SelectedLabels = selected.Length == 0 ? null : string.Join(",", selected);
        item.IsCorrect = isCorrect;
        item.SecondsSpent = request.SecondsSpent;
        item.AnsweredAt = DateTime.UtcNow;

        await uow.SaveChangesAsync(ct);

        // Everything except a timed mock gives feedback immediately. Review especially: seeing why
        // you missed it, at the moment you miss it again, is the whole point of the mode.
        var reveal = session.Mode != ExamMode.Exam;

        return new AnswerResponse(
            isCorrect,
            reveal ? correctLabels : [],
            reveal ? item.Question.Explanation : "",
            reveal);
    }
}

/// <summary>Scores a session and closes it. Safe to repeat: a closed session keeps its score.</summary>
public record SubmitExamCommand(Guid SessionId, [property: Required] string UserKey)
    : ICommand<ExamResultDto>;

public class SubmitExamHandler(IUnitOfWork uow, ExamReadModel read)
    : ICommandHandler<SubmitExamCommand, ExamResultDto>
{
    public async Task<ExamResultDto> HandleAsync(SubmitExamCommand request, CancellationToken ct)
    {
        var session = await read.LoadAsync(request.SessionId, request.UserKey, tracked: true, ct)
            ?? throw new KeyNotFoundException("Exam session not found.");

        var cert = session.Certification!;

        if (session.CompletedAt is null)
        {
            var scoredItems = session.Items.Where(i => i.IsScored).ToList();
            var correct = scoredItems.Count(i => i.IsCorrect == true);
            var score = scoredItems.Count == 0
                ? 0m
                : Math.Round(correct * 100m / scoredItems.Count, 2);
            var scaled = ExamReadModel.ScaledScore(score);

            session.CompletedAt = DateTime.UtcNow;
            session.ScorePercent = score;
            session.ScaledScore = scaled;
            session.Passed = scaled >= cert.PassingScaledScore;

            await uow.SaveChangesAsync(ct);
        }

        return ExamReadModel.ToResult(session, cert);
    }
}

// ---------- queries ----------

public record GetExamQuery(Guid SessionId, [property: Required] string UserKey)
    : IQuery<ExamSessionDto?>;

public class GetExamHandler(ExamReadModel read) : IQueryHandler<GetExamQuery, ExamSessionDto?>
{
    public async Task<ExamSessionDto?> HandleAsync(GetExamQuery request, CancellationToken ct)
    {
        var session = await read.LoadAsync(request.SessionId, request.UserKey, tracked: false, ct);
        if (session is null) return null;

        // Correct answers and explanations only ship to the client once the session is scored.
        // In-progress feedback comes from the answer command instead, so an unanswered question
        // never carries its own answer key.
        return ExamReadModel.ToDto(
            session, session.Certification!, revealAnswers: session.CompletedAt is not null);
    }
}

public record ExamHistoryQuery([property: Required] string UserKey, string? CertificationCode)
    : IQuery<IReadOnlyList<ExamResultDto>>;

public class ExamHistoryHandler(IUnitOfWork uow) : IQueryHandler<ExamHistoryQuery, IReadOnlyList<ExamResultDto>>
{
    /// <summary>Most recent sittings returned. A learner's history is a list, not an archive.</summary>
    private const int MaxSessions = 50;

    public async Task<IReadOnlyList<ExamResultDto>> HandleAsync(ExamHistoryQuery request, CancellationToken ct)
    {
        var query = uow.Repository<ExamSession>()
            .Query()
            .Include(s => s.Certification)
            .Include(s => s.Items).ThenInclude(i => i.Question).ThenInclude(q => q!.Domain)
            .Where(s => s.UserKey == request.UserKey && s.CompletedAt != null);

        if (!string.IsNullOrWhiteSpace(request.CertificationCode))
            query = query.Where(s => s.Certification!.Code == request.CertificationCode);

        var sessions = await query
            .OrderByDescending(s => s.CompletedAt)
            .Take(MaxSessions)
            .ToListAsync(ct);

        return sessions.Select(s => ExamReadModel.ToResult(s, s.Certification!)).ToList();
    }
}

/// <summary>
/// How many questions this learner has waiting in review, so the client can offer the mode with a
/// real number instead of sending them into an empty session.
/// </summary>
public record ReviewCountQuery([property: Required] string CertificationCode, [property: Required] string UserKey)
    : IQuery<ReviewCountDto>;

public class ReviewCountHandler(IUnitOfWork uow, ExamReadModel read)
    : IQueryHandler<ReviewCountQuery, ReviewCountDto>
{
    public async Task<ReviewCountDto> HandleAsync(ReviewCountQuery request, CancellationToken ct)
    {
        var certId = await uow.Repository<Certification>()
            .Query()
            .Where(c => c.Code == request.CertificationCode)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (certId is null) return new ReviewCountDto(request.CertificationCode, 0);

        var ids = await read.QuestionsToReviewAsync(certId.Value, request.UserKey, ct);

        // A question that has since been retired is still in the answer history but must not be
        // offered again, so the count has to match what a review session would actually serve.
        var count = await uow.Repository<Question>()
            .Query()
            .CountAsync(q => ids.Contains(q.Id) && q.RetiredReason == null, ct);

        return new ReviewCountDto(request.CertificationCode, count);
    }
}
