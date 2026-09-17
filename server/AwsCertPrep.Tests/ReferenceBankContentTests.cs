using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// The shipped reference banks, checked for the extraction noise that a PDF leaves behind.
///
/// These items came out of a third-party PDF, so the explanations arrived carrying the source's
/// answer-letter preambles ("The answer is AD.") and words split across a column wrap
/// ("on- premises"). Both matter to a learner: the explanation sits next to the options in
/// practice mode, so a letter in it is a spoiler, and a letter stops meaning anything at all if
/// the options are ever reordered. The bank is edited by hand and re-extracted by a script, so
/// nothing but this file keeps the noise from coming back.
/// </summary>
public class ReferenceBankContentTests
{
    private static readonly ReferenceBank Bank = new();

    public static TheoryData<string, ReferenceItem> AllItems()
    {
        var data = new TheoryData<string, ReferenceItem>();

        foreach (var code in Bank.CertificationCodes)
            foreach (var item in Bank.For(code))
                data.Add(code, item);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void Explanations_do_not_name_the_answer_letter(string code, ReferenceItem item)
    {
        string[] leaks = ["the answer is", "is correct.", "are correct.", "is right.", "is incorrect"];

        foreach (var leak in leaks)
            Assert.False(
                item.Explanation.Contains(leak, StringComparison.OrdinalIgnoreCase),
                $"{code} #{item.Number} names the answer: {item.Explanation}");

        Assert.False(
            System.Text.RegularExpressions.Regex.IsMatch(item.Explanation, @"(^|\s)[A-E]\.\s?[A-Z]"),
            $"{code} #{item.Number} leaks an option letter: {item.Explanation}");
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void No_word_is_left_split_across_a_pdf_column_wrap(string code, ReferenceItem item)
    {
        // "on- premises" - a hyphen with a lowercase letter on each side is always a wrap,
        // never punctuation.
        var texts = item.Options.Select(o => o.Text).Append(item.Stem).Append(item.Explanation);

        foreach (var text in texts)
            Assert.False(
                System.Text.RegularExpressions.Regex.IsMatch(text, "[a-z]- [a-z]"),
                $"{code} #{item.Number} has a word split by a column wrap: {text}");
    }

    [Fact]
    public void Repairing_a_column_wrap_never_changes_a_question_identity()
    {
        // Seeding matches a stored question to its bank item by the hash of the stem, and that
        // hash ignores punctuation. This is what lets a typographic repair in the shipped file
        // reach a database that was seeded before the fix: the row keeps its identity, so the
        // answer history pointing at it is not orphaned. If the hash ever became punctuation
        // sensitive, the repair would silently insert a second copy of the question instead.
        const string wrapped = "a dedicated network connection between a company's on- premises data center";
        const string repaired = "a dedicated network connection between a company's on-premises data center";

        Assert.Equal(QuestionHasher.Hash(wrapped), QuestionHasher.Hash(repaired));
        Assert.NotEqual(QuestionHasher.Hash(wrapped), QuestionHasher.Hash(repaired + " and the AWS Cloud"));
    }

    [Theory]
    [MemberData(nameof(AllItems))]
    public void Every_explanation_says_more_than_the_option_it_keys(string code, ReferenceItem item)
    {
        // An explanation that only restates the correct option teaches nothing, which is the
        // whole reason the item is in the bank rather than just its answer key.
        Assert.True(item.Explanation.Length >= 60, $"{code} #{item.Number}: {item.Explanation}");

        foreach (var option in item.CorrectOptions)
            Assert.NotEqual(
                option.Text.Trim().TrimEnd('.'),
                item.Explanation.Trim().TrimEnd('.'),
                StringComparer.OrdinalIgnoreCase);
    }
}
