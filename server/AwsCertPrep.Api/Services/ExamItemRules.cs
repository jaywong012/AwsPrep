using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// The official item format, from the AWS exam guides:
///   Multiple choice   - one correct response and three incorrect responses (so exactly 4 options).
///   Multiple response - two or more correct responses out of five or more response options.
/// Ordering and Matching also exist on AIF-C01 and MLA-C01 but are not generated yet.
///
/// One source of truth: the prompt tells the model these rules and the validator enforces them,
/// so drift in either direction is caught rather than silently stored.
/// </summary>
public static class ExamItemRules
{
    public const int MultipleChoiceOptionCount = 4;
    public const int MultipleResponseMinOptions = 5;
    public const int MultipleResponseMinCorrect = 2;

    /// <summary>
    /// The shape every multiple-response item in the CLF-C02 reference bank actually has:
    /// 12 of 12 are five options with exactly two keyed correct. Foundational exams never ask
    /// for three, so a six-option or three-correct item is model drift, not an exam item.
    /// Higher tiers do ask "(Select THREE.)", so they keep the official minimums above.
    /// </summary>
    public const int FoundationalMultipleResponseOptionCount = 5;
    public const int FoundationalMultipleResponseCorrectCount = 2;

    /// <summary>Foundational stems stay short; scenario length is an Associate-level trait.</summary>
    public const int FoundationalMaxStemWords = 70;
    public const int AssociateMaxStemWords = 130;

    public static int MaxStemWords(CertificationLevel level) =>
        level == CertificationLevel.Foundational ? FoundationalMaxStemWords : AssociateMaxStemWords;

    /// <summary>
    /// The length band real items sit in, as opposed to the hard cap above. Measured on the
    /// CLF-C02 reference bank: shortest stem 9 words, median 23, 95th percentile 49, longest 65.
    /// The cap alone is not enough guidance - a model told only "under 70 words" writes 65-word
    /// stems for every item, which reads nothing like the real exam.
    /// </summary>
    public static string TypicalStemLength(CertificationLevel level) =>
        level == CertificationLevel.Foundational
            ? "Most real items run 15-35 words; only a scenario item ever passes 50."
            : "Most real items run 40-90 words; the scenario carries the length, not the question sentence.";

    /// <summary>
    /// Hedges real AWS items never use: they make an option unfalsifiable, so a candidate
    /// cannot reason to a single best answer.
    /// </summary>
    public static readonly string[] BannedPhrases =
    [
        "if supported",
        "if available",
        "if applicable",
        "if necessary",
        "as needed",
        "and/or",
        "all of the above",
        "none of the above",
        "both a and b",
        "any of these",
        "it depends",
    ];

    /// <summary>
    /// Topics the foundational exam guides explicitly place outside the target candidate's
    /// job tasks (model building, tuning, statistical analysis). An item that hinges on any
    /// of these is an Associate/Specialty item wearing a foundational label.
    /// </summary>
    public static readonly string[] FoundationalOutOfScopeTerms =
    [
        "dropout",
        "weight decay",
        "l2 regularization",
        "l1 regularization",
        "hyperparameter tun",
        "learning rate",
        "mini-batch",
        "batch size",
        "epoch",
        "gradient descent",
        "backpropagation",
        "early stopping",
        "cross-validation fold",
        "quantiz",
        "feature engineering pipeline",
        "confusion matrix threshold",
        "softmax",
        "activation function",
    ];

    public static IReadOnlyList<string> OutOfScopeTerms(CertificationLevel level) =>
        level == CertificationLevel.Foundational ? FoundationalOutOfScopeTerms : [];

    /// <summary>What "Hard" means at each level, so difficulty never drifts up a tier.</summary>
    public static string DifficultyGuidance(CertificationLevel level, Difficulty difficulty) =>
        (level, difficulty) switch
        {
            (CertificationLevel.Foundational, Difficulty.Easy) =>
                "Direct recall: name the service or concept that fits a one-sentence need.",
            (CertificationLevel.Foundational, Difficulty.Medium) =>
                "Applied recognition: a two-sentence business situation where the candidate picks the right service or concept.",
            (CertificationLevel.Foundational, Difficulty.Hard) =>
                "Still foundational. Hard means the distractors are closely related services or easily confused concepts - NOT deeper technical work. "
                + "Never require designing an architecture, tuning a model, writing code, choosing instance types, or combining three or more requirements.",
            (CertificationLevel.Associate, Difficulty.Easy) =>
                "A short scenario with one clear requirement and a single best service choice.",
            (CertificationLevel.Associate, Difficulty.Medium) =>
                "A scenario with two requirements (for example availability plus cost) where trade-offs decide the answer.",
            (CertificationLevel.Associate, Difficulty.Hard) =>
                "A scenario with competing constraints where every option is technically possible but only one is best on the stated criteria (cost, operational overhead, or latency).",
            _ => "Match the exam's usual difficulty for this certification level.",
        };
}
