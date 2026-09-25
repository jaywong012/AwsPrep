using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Dtos;

// ---------- certifications ----------

public record DomainDto(int Id, string Name, decimal WeightPercent, int QuestionCount);

public record CertificationDto(
    int Id,
    string Code,
    string Name,
    string Description,
    CertificationLevel Level,
    int PassingScore,
    int PassingScaledScore,
    int ExamQuestionCount,
    int ScoredQuestionCount,
    int UnscoredQuestionCount,
    int DurationMinutes,
    string QuestionTypes,
    string TargetCandidate,
    string ExamGuideUrl,
    int QuestionCount,
    int RetiredQuestionCount,
    IReadOnlyList<DomainDto> Domains);

// ---------- questions ----------

public record OptionDto(string Label, string Text, bool? IsCorrect);

public record QuestionDto(
    Guid Id,
    string Stem,
    QuestionType Type,
    Difficulty Difficulty,
    QuestionSource Source,
    string? DomainName,
    string? ServiceTags,
    string? Explanation,
    IReadOnlyList<OptionDto> Options);

public class GenerateQuestionsRequest
{
    [Required] public string CertificationCode { get; set; } = "";
    public int? DomainId { get; set; }
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;

    [Range(1, 20)] public int Count { get; set; } = 5;

    /// <summary>Optional free-text focus, e.g. "prompt engineering techniques".</summary>
    [MaxLength(300)] public string? TopicHint { get; set; }
}

public record GenerateQuestionsResponse(
    string Provider,
    string Model,
    int Requested,
    int Created,
    int Duplicates,
    int Rejected,
    IReadOnlyList<string> RejectionReasons,
    IReadOnlyList<QuestionDto> Questions,
    string? Warning);

// ---------- format audit ----------

public record AuditReasonDto(string Reason, int Count);

public record AuditFindingDto(
    Guid QuestionId,
    string CertificationCode,
    Difficulty Difficulty,
    QuestionType Type,
    string Stem,
    string Reason,
    bool AlreadyRetired);

public record AuditReportDto(
    int Flagged,
    int TotalQuestions,
    IReadOnlyList<AuditReasonDto> Reasons,
    IReadOnlyList<AuditFindingDto> Findings);

public record AuditCleanupDto(
    int Flagged,
    int Deleted,
    int Retired,
    IReadOnlyList<AuditReasonDto> Reasons);

// ---------- exams ----------

public class StartExamRequest
{
    [Required] public string CertificationCode { get; set; } = "";
    public ExamMode Mode { get; set; } = ExamMode.Practice;

    /// <summary>Null = use the certification's official exam length.</summary>
    [Range(1, 100)] public int? QuestionCount { get; set; }

    public int? DomainId { get; set; }
    public Difficulty? Difficulty { get; set; }
}

public record ExamSessionDto(
    Guid Id,
    string CertificationCode,
    string CertificationName,
    ExamMode Mode,
    int DurationMinutes,
    int PassingScore,
    DateTime StartedAt,
    DateTime? CompletedAt,
    decimal? ScorePercent,
    bool? Passed,
    IReadOnlyList<ExamItemDto> Items);

public record ExamItemDto(
    int Order,
    Guid QuestionId,
    QuestionDto Question,
    string? SelectedLabels,
    bool? IsCorrect);

public class AnswerRequest
{
    [Required] public Guid QuestionId { get; set; }
    public string[] SelectedLabels { get; set; } = [];
    [Range(0, 7200)] public int SecondsSpent { get; set; }
}

public record AnswerResponse(
    bool IsCorrect,
    string[] CorrectLabels,
    string Explanation,
    bool RevealAnswer);

public record ExamResultDto(
    Guid SessionId,
    string CertificationCode,
    /// <summary>Percentage of scored items answered correctly.</summary>
    decimal ScorePercent,
    /// <summary>Estimated score on the official 100-1000 scale.</summary>
    int ScaledScore,
    int PassingScaledScore,
    bool Passed,
    /// <summary>Equivalent percentage threshold for the scaled pass mark.</summary>
    int PassingScore,
    int Correct,
    /// <summary>Items that counted toward the score.</summary>
    int ScoredTotal,
    /// <summary>All items delivered, including unscored trial items.</summary>
    int Total,
    int Unanswered,
    int SecondsSpent,
    IReadOnlyList<DomainScoreDto> DomainBreakdown);

public record DomainScoreDto(string Domain, int Correct, int Total, decimal AccuracyPercent);

/// <summary>Questions whose most recent answer from this learner was wrong.</summary>
public record ReviewCountDto(string CertificationCode, int Count);

// ---------- difficulty recalculation ----------

public record DifficultyTallyDto(int Easy, int Medium, int Hard);

public record DifficultyChangeDto(Guid QuestionId, string Stem, Difficulty From, Difficulty To);

public record DifficultyRecalculationDto(
    string? CertificationCode,
    /// <summary>False for a dry run, where nothing was written.</summary>
    bool Applied,
    int Examined,
    int Changed,
    DifficultyTallyDto Before,
    DifficultyTallyDto After,
    /// <summary>A sample of the changes, not the whole list.</summary>
    IReadOnlyList<DifficultyChangeDto> Sample);

// ---------- insights (ML.NET) ----------

public record ReadinessDto(
    string CertificationCode,
    string Method,
    decimal PredictedScorePercent,
    int PassingScore,
    bool LikelyToPass,
    decimal Confidence,
    int AnswersAnalyzed,
    IReadOnlyList<DomainReadinessDto> Domains,
    IReadOnlyList<string> Recommendations);

public record DomainReadinessDto(
    string Domain,
    decimal WeightPercent,
    int Answered,
    decimal ObservedAccuracy,
    decimal PredictedAccuracy,
    string Status);

// ---------- lessons ----------

/// <summary>How well the learner is doing on a topic, derived from their own answered items.</summary>
public enum MasteryStatus
{
    /// <summary>Too few answers on this topic to say anything yet.</summary>
    Untested = 0,
    Weak = 1,
    Learning = 2,
    Strong = 3
}

/// <summary>What the accuracy figure was measured over.</summary>
public enum MasteryBasis
{
    /// <summary>Questions tagged with this topic's own service. The precise case.</summary>
    Topic = 0,
    /// <summary>
    /// The whole exam domain. Used for concept topics that no service name identifies, so the
    /// figure describes the domain rather than the topic and must be labelled as such.
    /// </summary>
    Domain = 1
}

public record LessonMasteryDto(
    int Answered,
    int Correct,
    /// <summary>Null until the learner has answered enough items for the figure to mean anything.</summary>
    decimal? AccuracyPercent,
    MasteryStatus Status,
    MasteryBasis Basis);

public record LessonSummaryDto(
    string Slug,
    string Title,
    string Category,
    /// <summary>Service, Concept or Commercial - see LessonCatalog.LessonKind.</summary>
    string Kind,
    string? DomainName,
    string Purpose,
    bool IsCore,
    /// <summary>True once the detailed notes have been generated and cached for this topic.</summary>
    bool HasNotes,
    bool Completed,
    LessonMasteryDto Mastery);

public record LessonsResponse(
    string CertificationCode,
    string CertificationName,
    int TotalTopics,
    int CompletedTopics,
    /// <summary>False when no provider is set up, so the client can say why notes are missing.</summary>
    bool AiConfigured,
    /// <summary>Already ordered by what the learner should study next.</summary>
    IReadOnlyList<LessonSummaryDto> Topics);

public record LessonBodyDto(
    string Overview,
    IReadOnlyList<string> UseCases,
    string CostNotes,
    IReadOnlyList<string> Integrations,
    string RealWorldExample,
    IReadOnlyList<string> ExamTraps,
    string Provider,
    string Model,
    DateTime GeneratedAt);

public record LessonLinkDto(string Slug, string Title);

public record LessonDetailDto(
    string Slug,
    string Title,
    string Category,
    string? DomainName,
    decimal? DomainWeightPercent,
    string Purpose,
    string PricingModel,
    string DocsUrl,
    string? PricingUrl,
    LessonMasteryDto Mastery,
    bool Completed,
    /// <summary>
    /// Null until the notes have been generated for this topic. Reading a lesson never generates
    /// them, so the client asks for them separately once it has rendered the verified facts.
    /// </summary>
    LessonBodyDto? Body,
    bool AiConfigured,
    string? Warning,
    IReadOnlyList<QuestionDto> PracticeQuestions,
    IReadOnlyList<LessonLinkDto> Related,
    /// <summary>The lesson before this one in curriculum order; null on the first topic.</summary>
    LessonLinkDto? Previous,
    /// <summary>The lesson after this one in curriculum order; null on the last topic.</summary>
    LessonLinkDto? Next);

public record LessonProgressDto(string Slug, bool Completed, DateTime? CompletedAt);

// ---------- lesson tutor ----------

public record TutorTurnDto(string Question, string Answer);

public class TutorAskRequest
{
    [Required]
    [MaxLength(500)]
    public string Question { get; set; } = "";

    /// <summary>
    /// Earlier turns the client wants remembered. Held by the client rather than the server so a
    /// conversation needs no table to store and expire; only the most recent few are replayed.
    /// </summary>
    public List<TutorTurnDto>? History { get; set; }
}

public record TutorAnswerDto(
    string Slug,
    string Title,
    string Answer,
    /// <summary>Who answered. Shown to the learner, the same way the notes name their author.</summary>
    string Provider,
    string Model);

public class LessonProgressRequest
{
    public bool Completed { get; set; }
}

// ---------- auth ----------

/// <summary>
/// What the SPA gets back from registering or signing in. <paramref name="ExpiresAtUtc"/> lets
/// the client renew before a request fails rather than discovering expiry as a 401 mid-exam.
/// </summary>
public record AuthResultDto(string Token, DateTime ExpiresAtUtc, string UserId, string Email);

/// <summary>Who the caller is, for the SPA to display and to re-key its per-user caches on.</summary>
public record CurrentUserDto(string UserId, string Email);

public class CredentialsRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
