using System.ComponentModel.DataAnnotations;

namespace AwsCertPrep.Api.Application.Options;

/// <summary>
/// Tuning for the lessons feature.
///
/// These were constants inside the service. They are configuration because they are judgement
/// calls, not invariants: what counts as "weak", how many practice questions to attach, how many
/// answers make an accuracy figure meaningful. Changing a teaching decision should not need a
/// rebuild, and having them in one bound, validated place makes the whole policy readable at once.
/// </summary>
public class LessonOptions
{
    public const string SectionName = "Lessons";

    /// <summary>Answers needed on a topic before an accuracy figure means anything.</summary>
    [Range(1, 50)]
    public int MinAnswersForMastery { get; set; } = 2;

    /// <summary>Below this accuracy a topic is Weak and sorts to the top of the list.</summary>
    [Range(0, 100)]
    public decimal WeakThresholdPercent { get; set; } = 60m;

    /// <summary>At or above this accuracy a topic is Strong and sinks to the bottom.</summary>
    [Range(0, 100)]
    public decimal StrongThresholdPercent { get; set; } = 85m;

    /// <summary>Questions from the learner's own bank offered at the foot of a lesson.</summary>
    [Range(0, 20)]
    public int PracticeQuestionCount { get; set; } = 3;

    /// <summary>Cross-links shown at the foot of a lesson.</summary>
    [Range(0, 20)]
    public int RelatedTopicCount { get; set; } = 4;

    /// <summary>Sibling topics offered to the model as cross-reference targets when writing notes.</summary>
    [Range(0, 60)]
    public int CrossReferenceCount { get; set; } = 20;
}

/// <summary>Tuning for the ask-about-this-lesson tutor.</summary>
public class TutorOptions
{
    public const string SectionName = "Tutor";

    /// <summary>Earlier turns replayed to the model. Caps how large one prompt can grow.</summary>
    [Range(0, 20)]
    public int MaxHistoryTurns { get; set; } = 6;

    /// <summary>Longest question accepted, so one request cannot become an unbounded prompt.</summary>
    [Range(50, 4000)]
    public int MaxQuestionLength { get; set; } = 500;

    /// <summary>Longest earlier answer replayed, trimmed so history cannot dominate the prompt.</summary>
    [Range(100, 8000)]
    public int MaxRepeatedAnswerLength { get; set; } = 1200;
}

/// <summary>Tuning for question generation.</summary>
public class QuestionOptions
{
    public const string SectionName = "Questions";

    /// <summary>Real reference items shown to the model as style exemplars.</summary>
    [Range(0, 10)]
    public int ExemplarCount { get; set; } = 3;

    /// <summary>A topic with fewer questions than this counts as a coverage gap.</summary>
    [Range(1, 50)]
    public int CoveredThreshold { get; set; } = 2;

    /// <summary>Existing stems sent to the model so it does not repeat them.</summary>
    [Range(0, 200)]
    public int ExistingStemSampleSize { get; set; } = 40;

    /// <summary>Undercovered topics fed back into the prompt to widen coverage.</summary>
    [Range(0, 50)]
    public int UndercoveredTopicCount { get; set; } = 10;
}

/// <summary>Tuning for exam sessions.</summary>
public class ExamOptions
{
    public const string SectionName = "Exams";

    /// <summary>
    /// Slack allowed past a timed exam's deadline. The countdown runs in the browser, so a
    /// last-second answer must not be rejected because the two clocks disagree slightly.
    /// </summary>
    [Range(0, 300)]
    public int DeadlineGraceSeconds { get; set; } = 30;
}

/// <summary>Tuning for the request pipeline.</summary>
public class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>
    /// Above this, a request is logged at Information rather than Debug. Provider-backed requests
    /// are slow by nature, so the threshold exists to separate "slow because it called a model"
    /// from "slow unexpectedly".
    /// </summary>
    [Range(100, 120000)]
    public int SlowRequestMilliseconds { get; set; } = 2000;
}

public static class StudyOptionsRegistration
{
    /// <summary>
    /// Binds every tuning section and validates it at startup, so a bad value fails the app
    /// immediately with the offending setting named rather than behaving oddly much later.
    /// </summary>
    public static IServiceCollection AddStudyOptions(this IServiceCollection services, IConfiguration configuration)
    {
        Bind<LessonOptions>(services, configuration, LessonOptions.SectionName);
        Bind<TutorOptions>(services, configuration, TutorOptions.SectionName);
        Bind<QuestionOptions>(services, configuration, QuestionOptions.SectionName);
        Bind<ExamOptions>(services, configuration, ExamOptions.SectionName);
        Bind<PipelineOptions>(services, configuration, PipelineOptions.SectionName);

        return services;
    }

    private static void Bind<T>(IServiceCollection services, IConfiguration configuration, string section)
        where T : class
    {
        services.AddOptions<T>()
            .Bind(configuration.GetSection(section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
