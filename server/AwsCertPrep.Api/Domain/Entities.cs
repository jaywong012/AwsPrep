namespace AwsCertPrep.Api.Domain;

public enum QuestionType
{
    SingleChoice = 0,
    MultipleChoice = 1
}

public enum Difficulty
{
    Easy = 0,
    Medium = 1,
    Hard = 2
}

public enum QuestionSource
{
    Ai = 0,
    Seed = 1,
    Manual = 2,
    /// <summary>Imported from a real exam-style reference bank rather than generated.</summary>
    Reference = 3
}

public enum ExamMode
{
    /// <summary>Immediate feedback after each question.</summary>
    Practice = 0,
    /// <summary>Timed, feedback only at the end.</summary>
    Exam = 1,

    /// <summary>
    /// Only the questions this learner last got wrong, with feedback after each one. The point of
    /// practice is repetition on what you actually missed, so this is the mode that turns a bank
    /// of questions into revision.
    /// </summary>
    Review = 2
}

public enum CertificationLevel
{
    Foundational = 0,
    Associate = 1,
    Professional = 2,
    Specialty = 3
}

public class Certification
{
    public int Id { get; set; }
    public string Code { get; set; } = "";          // e.g. AIF-C01
    public string Name { get; set; } = "";          // AWS Certified AI Practitioner
    public string Description { get; set; } = "";
    public CertificationLevel Level { get; set; }

    /// <summary>
    /// Derived percentage of scored questions needed, approximated from
    /// PassingScaledScore. AWS does not publish the real raw-to-scaled mapping.
    /// </summary>
    public int PassingScore { get; set; }

    /// <summary>Minimum scaled score on the official 100-1000 scale (700 or 720).</summary>
    public int PassingScaledScore { get; set; }

    /// <summary>Total questions delivered on the real exam (scored + unscored).</summary>
    public int ExamQuestionCount { get; set; }

    /// <summary>Questions that affect the score (50 on all four exams).</summary>
    public int ScoredQuestionCount { get; set; }

    /// <summary>Unscored trial questions, not identified during the exam (15).</summary>
    public int UnscoredQuestionCount { get; set; }

    public int DurationMinutes { get; set; }

    /// <summary>Who the exam is written for; keeps generated items at the right altitude.</summary>
    public string TargetCandidate { get; set; } = "";

    /// <summary>Newline-separated job tasks the exam guide puts out of scope.</summary>
    public string OutOfScopeTasks { get; set; } = "";

    /// <summary>Comma-separated official question types, e.g. "Multiple choice, Multiple response".</summary>
    public string QuestionTypes { get; set; } = "";

    public string ExamGuideUrl { get; set; } = "";

    public List<CertificationDomain> Domains { get; set; } = [];
    public List<Question> Questions { get; set; } = [];
}

public class CertificationDomain
{
    public int Id { get; set; }
    public int CertificationId { get; set; }
    public Certification? Certification { get; set; }

    public string Name { get; set; } = "";
    public decimal WeightPercent { get; set; }
}

public class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CertificationId { get; set; }
    public Certification? Certification { get; set; }

    public int? DomainId { get; set; }
    public CertificationDomain? Domain { get; set; }

    public string Stem { get; set; } = "";
    public QuestionType Type { get; set; }
    public Difficulty Difficulty { get; set; }
    public QuestionSource Source { get; set; }

    public string Explanation { get; set; } = "";
    public string? ServiceTags { get; set; }        // comma separated, e.g. "Amazon Bedrock,SageMaker"
    public string? Model { get; set; }              // LLM that produced it
    public string StemHash { get; set; } = "";      // dedupe guard
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when an audit finds the item does not match the official exam format or the
    /// certification's scope. Retired items stay in the database (exam history references
    /// them) but are excluded from new sessions and from the default bank listing.
    /// </summary>
    public string? RetiredReason { get; set; }

    public DateTime? RetiredAt { get; set; }

    public List<QuestionOption> Options { get; set; } = [];
}

public class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public string Label { get; set; } = "";         // A, B, C, D, E
    public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
}

public class ExamSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int CertificationId { get; set; }
    public Certification? Certification { get; set; }

    /// <summary>
    /// The owning account's id, from the authenticated principal. No default: every query scopes
    /// on this by hand, so a row written with a blank key belongs to nobody and no learner will
    /// ever see it again. It was once a self-asserted browser key, which is why the column is a
    /// string rather than a foreign key.
    /// </summary>
    public string UserKey { get; set; } = string.Empty;
    public ExamMode Mode { get; set; }
    public int DurationMinutes { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Percentage of scored items answered correctly.</summary>
    public decimal? ScorePercent { get; set; }

    /// <summary>Estimated score on the official 100-1000 scale.</summary>
    public int? ScaledScore { get; set; }

    public bool? Passed { get; set; }

    public List<ExamSessionQuestion> Items { get; set; } = [];
}

public class ExamSessionQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ExamSessionId { get; set; }
    public ExamSession? ExamSession { get; set; }

    public Guid QuestionId { get; set; }
    public Question? Question { get; set; }

    public int Order { get; set; }

    /// <summary>
    /// False for the unscored trial questions a mock exam includes to mirror the real
    /// 50-scored-of-65 format. Not surfaced to the candidate, exactly as on the real exam.
    /// </summary>
    public bool IsScored { get; set; } = true;

    public string? SelectedLabels { get; set; }      // "A" or "A,C"
    public bool? IsCorrect { get; set; }
    public int SecondsSpent { get; set; }
    public DateTime? AnsweredAt { get; set; }
}
