using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// The near-duplicate guard on generation.
///
/// The stem hash only ever caught a stem repeated character for character, so a bank built up
/// over many generation runs collected paraphrase pairs: the same question asked twice in
/// slightly different words, both of which a practice set can then draw. Every "reworded" case
/// below is a real pair found in the CLF-C02 bank; every "different" case is a pair that shares
/// a service or a phrasing template but asks something else, and must keep getting through.
/// </summary>
public class StemSimilarityTests
{
    [Theory]
    [InlineData(
        "Which AWS Well-Architected Framework pillar focuses on using computing resources efficiently to meet system requirements?",
        "Which pillar of the AWS Well-Architected Framework focuses on using computing resources efficiently to meet system requirements?")]
    [InlineData(
        "A company wants to protect its web application from common exploits such as SQL injection and cross-site scripting. Which AWS service should the company use?",
        "A company needs to protect its web application from common exploits such as SQL injection and cross-site scripting. Which AWS service should the company use?")]
    [InlineData(
        "Which AWS global infrastructure component is used to cache content closer to end users to reduce latency?",
        "Which AWS global infrastructure component is used to cache content closer to users to reduce latency?")]
    public void A_reworded_stem_is_recognised_as_a_duplicate(string first, string second)
    {
        Assert.True(
            StemSimilarity.IsRewordingOfAny(second, [StemSimilarity.Fingerprint(first)]),
            $"overlap was {StemSimilarity.Overlap(StemSimilarity.Fingerprint(first), StemSimilarity.Fingerprint(second)):F2}");
    }

    [Theory]
    [InlineData(
        "Which two statements about Amazon CloudFront are true? (Select TWO.)",
        "Which two statements about Amazon Aurora are true? (Select TWO.)")]
    [InlineData(
        "Which AWS service can run SQL queries directly on data stored in Amazon S3 without loading the data into a separate database?",
        "Which AWS service is a petabyte-scale data warehouse for analytics over structured data?")]
    [InlineData(
        "Which pillar of the AWS Well-Architected Framework focuses on minimizing the environmental impact of running cloud workloads?",
        "Which pillar of the AWS Well-Architected Framework focuses on the ability of a system to recover from failures?")]
    [InlineData(
        "A company needs to encrypt data across multiple AWS services and wants a centralized way to manage the encryption keys. Which AWS service should the company use?",
        "A company wants to establish a schedule for rotating database user credentials. Which AWS service will support this requirement?")]
    public void Two_different_questions_are_not_treated_as_duplicates(string first, string second)
    {
        Assert.False(
            StemSimilarity.IsRewordingOfAny(second, [StemSimilarity.Fingerprint(first)]),
            $"overlap was {StemSimilarity.Overlap(StemSimilarity.Fingerprint(first), StemSimilarity.Fingerprint(second)):F2}");
    }

    [Fact]
    public void Framing_words_alone_do_not_make_two_stems_similar()
    {
        // Both are pure framing plus one distinct noun. Without the framing list these would
        // overlap almost completely and every scenario question would look like a duplicate.
        var a = StemSimilarity.Fingerprint("A company wants to use an AWS service. Which service should the company use?");
        var b = StemSimilarity.Fingerprint("A company needs to use an AWS service. Which service will meet these requirements?");

        Assert.True(StemSimilarity.Overlap(a, b) < StemSimilarity.DuplicateThreshold);
    }

    [Fact]
    public void A_pair_rewritten_word_for_word_is_a_known_miss()
    {
        // Both ask which deployment model a single-tenant environment is, and both are in the
        // bank. They share almost no words, so a word-overlap measure cannot see it and the
        // threshold that would catch it flags unrelated questions instead. Written down rather
        // than left as a surprise: this guard catches rewordings, not restatements.
        var a = StemSimilarity.Fingerprint(
            "A company wants a cloud deployment model that is provisioned for exclusive use by a single organization. Which cloud deployment model does this describe?");
        var b = StemSimilarity.Fingerprint(
            "A company wants a cloud deployment model that is used exclusively by one organization and is not shared with other tenants. Which model should the company choose?");

        Assert.True(StemSimilarity.Overlap(a, b) < StemSimilarity.DuplicateThreshold);
    }

    [Fact]
    public void An_empty_stem_matches_nothing()
    {
        Assert.False(StemSimilarity.IsRewordingOfAny("", [StemSimilarity.Fingerprint("Which AWS service stores objects?")]));
        Assert.Equal(0, StemSimilarity.Overlap([], StemSimilarity.Fingerprint("Which AWS service stores objects?")));
    }
}
