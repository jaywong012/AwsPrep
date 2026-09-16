using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// Difficulty inferred from an item's shape.
///
/// Background: the Difficulty column used to record two different things - "this item reads as
/// Medium" for reference items, and "Medium was requested" for generated ones. A bulk fill that
/// asked for Medium every time left 493 of 653 questions labelled Medium, which made the filter
/// and the readiness model close to useless. This rater is the single source of truth that
/// replaced it, so the ladder it implements is worth pinning down.
/// </summary>
public class QuestionDifficultyRaterTests
{
    [Fact]
    public void A_short_recall_question_is_easy()
    {
        var difficulty = QuestionDifficultyRater.Rate(
            "Which AWS service provides object storage?", correctOptionCount: 1);

        Assert.Equal(Difficulty.Easy, difficulty);
    }

    [Theory]
    [InlineData("A company needs to store backups.")]
    [InlineData("An organization wants to reduce costs.")]
    [InlineData("A developer needs to process uploads.")]
    [InlineData("A team must retain audit logs.")]
    public void A_scenario_opener_makes_a_short_stem_medium(string stem)
    {
        // These openers are how the real exam signals an applied question rather than a
        // definition, even when the stem itself is short.
        Assert.Equal(Difficulty.Medium, QuestionDifficultyRater.Rate(stem, correctOptionCount: 1));
    }

    [Fact]
    public void A_long_stem_is_medium_even_without_a_scenario_opener()
    {
        var stem = string.Join(' ', Enumerable.Repeat("word", 31)) + "?";

        Assert.Equal(Difficulty.Medium, QuestionDifficultyRater.Rate(stem, correctOptionCount: 1));
    }

    [Theory]
    [InlineData("Which option is the MOST cost-effective?")]
    [InlineData("Which approach requires the LEAST operational overhead?")]
    [InlineData("Which service is the BEST fit?")]
    [InlineData("Which design gives the HIGHEST availability?")]
    public void A_capitalised_comparative_makes_it_hard(string stem)
    {
        // A capitalised qualifier is the exam's way of saying several options work and only one
        // is best, which is the harder task regardless of how short the stem is.
        Assert.Equal(Difficulty.Hard, QuestionDifficultyRater.Rate(stem, correctOptionCount: 1));
    }

    [Fact]
    public void A_lowercase_comparative_is_not_treated_as_a_qualifier()
    {
        // "most" in ordinary prose is not the exam's signal, and treating it as one would push
        // most of the bank back into Hard.
        var difficulty = QuestionDifficultyRater.Rate(
            "Which service stores the most data?", correctOptionCount: 1);

        Assert.Equal(Difficulty.Easy, difficulty);
    }

    [Fact]
    public void Selecting_more_than_one_answer_is_hard()
    {
        var difficulty = QuestionDifficultyRater.Rate(
            "Which two actions can a lifecycle policy perform? (Select TWO.)", correctOptionCount: 2);

        Assert.Equal(Difficulty.Hard, difficulty);
    }

    [Fact]
    public void Multi_answer_beats_a_scenario_opener()
    {
        // Both rules match; the harder one has to win, or every multi-answer scenario question
        // would be filed as Medium.
        var difficulty = QuestionDifficultyRater.Rate(
            "A company needs to secure its data. Which two actions should it take? (Select TWO.)",
            correctOptionCount: 2);

        Assert.Equal(Difficulty.Hard, difficulty);
    }

    [Fact]
    public void Rating_the_same_stem_twice_gives_the_same_answer()
    {
        // The recalculation endpoint is run repeatedly against the whole bank, so it has to be
        // idempotent: a second run must change nothing.
        const string stem = "A company needs to reduce its MOST expensive storage costs.";

        Assert.Equal(
            QuestionDifficultyRater.Rate(stem, 1),
            QuestionDifficultyRater.Rate(stem, 1));
    }
}
