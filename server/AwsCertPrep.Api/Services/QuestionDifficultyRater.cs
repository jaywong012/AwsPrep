using System.Text.RegularExpressions;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Rates an item's difficulty from its shape.
///
/// This exists because the Difficulty column was recording two different things. On a reference
/// item it meant "this item reads as Medium", inferred here. On a generated item it meant "Medium
/// was requested" - which says nothing about what the model actually wrote, and left 493 of 653
/// questions labelled Medium because a bulk fill asked for Medium every time. One column with two
/// meanings is worse than either, and the filters and the readiness model both consume it.
///
/// The ladder matches the one the generation prompt describes: recall, applied recognition, then a
/// trade-off where more than one option works.
/// </summary>
public static class QuestionDifficultyRater
{
    /// <summary>
    /// A capitalised comparative ("MOST cost-effective", "LEAST operational overhead") is how the
    /// real exams signal that several options work and only one is best.
    /// </summary>
    private static readonly Regex QualifierPattern =
        new(@"\b(MOST|LEAST|BEST|FEWEST|LOWEST|HIGHEST|GREATEST)\b", RegexOptions.Compiled);

    /// <summary>Above this many words a stem is carrying a scenario rather than a bare question.</summary>
    private const int ScenarioWordCount = 30;

    private static readonly string[] ScenarioOpeners =
        ["A company", "An organization", "An organisation", "A user", "A developer", "A team"];

    public static Difficulty Rate(string stem, int correctOptionCount)
    {
        // Picking two right answers, or picking the best of several that work, is the harder task.
        if (correctOptionCount > 1 || QualifierPattern.IsMatch(stem)) return Difficulty.Hard;

        var words = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (words > ScenarioWordCount
            || ScenarioOpeners.Any(o => stem.Contains(o, StringComparison.OrdinalIgnoreCase)))
            return Difficulty.Medium;

        return Difficulty.Easy;
    }
}
