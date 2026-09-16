using AwsCertPrep.Api.Data;

namespace AwsCertPrep.Tests;

/// <summary>
/// The hand-written curriculum, checked against the shape the database will accept.
///
/// These exist because a single over-long Purpose took the whole API down on startup: seeding
/// runs before the app serves traffic, so SQL Server rejecting one row is not a bad lesson, it is
/// no application at all. The catalogue is edited by hand and the column limits live in
/// AppDbContext, far away from it, so nothing connected the two until this file did.
/// </summary>
public class LessonCatalogTests
{
    // Mirrors the HasMaxLength calls in AppDbContext.OnModelCreating for LessonTopic.
    private const int MaxSlug = 100;
    private const int MaxTitle = 200;
    private const int MaxCategory = 100;
    private const int MaxPurpose = 600;
    private const int MaxPricingModel = 600;
    private const int MaxUrl = 400;
    private const int MaxServiceTags = 600;

    public static TheoryData<string, LessonCatalog.CatalogTopic> AllTopics()
    {
        var data = new TheoryData<string, LessonCatalog.CatalogTopic>();

        foreach (var (code, topics) in LessonCatalog.ByCertification)
            foreach (var topic in topics)
                data.Add(code, topic);

        return data;
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_field_fits_its_column(string code, LessonCatalog.CatalogTopic topic)
    {
        Assert.True(topic.Slug.Length <= MaxSlug, $"{code}/{topic.Slug}: slug is {topic.Slug.Length}");
        Assert.True(topic.Title.Length <= MaxTitle, $"{code}/{topic.Slug}: title is {topic.Title.Length}");
        Assert.True(topic.Category.Length <= MaxCategory, $"{code}/{topic.Slug}: category is {topic.Category.Length}");
        Assert.True(topic.Purpose.Length <= MaxPurpose, $"{code}/{topic.Slug}: purpose is {topic.Purpose.Length}");
        Assert.True(topic.PricingModel.Length <= MaxPricingModel, $"{code}/{topic.Slug}: pricing model is {topic.PricingModel.Length}");
        Assert.True(topic.DocsUrl.Length <= MaxUrl, $"{code}/{topic.Slug}: docs URL is {topic.DocsUrl.Length}");
        Assert.True((topic.PricingUrl?.Length ?? 0) <= MaxUrl, $"{code}/{topic.Slug}: pricing URL is too long");
        Assert.True((topic.ServiceTags?.Length ?? 0) <= MaxServiceTags, $"{code}/{topic.Slug}: service tags are too long");
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_required_field_is_populated(string code, LessonCatalog.CatalogTopic topic)
    {
        Assert.False(string.IsNullOrWhiteSpace(topic.Slug), $"{code}: a topic has no slug");
        Assert.False(string.IsNullOrWhiteSpace(topic.Title), $"{code}/{topic.Slug}: no title");
        Assert.False(string.IsNullOrWhiteSpace(topic.Purpose), $"{code}/{topic.Slug}: no purpose");
        Assert.False(string.IsNullOrWhiteSpace(topic.PricingModel), $"{code}/{topic.Slug}: no pricing model");
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Urls_are_absolute_https_aws_links(string code, LessonCatalog.CatalogTopic topic)
    {
        // Not a check that the page exists - that needs the network - but a typo'd or relative
        // URL is a broken "confirm this against AWS" link, which is the one thing the seeded half
        // of a lesson promises.
        AssertAwsUrl(topic.DocsUrl, $"{code}/{topic.Slug} docs");
        if (topic.PricingUrl is { } pricing) AssertAwsUrl(pricing, $"{code}/{topic.Slug} pricing");
    }

    private static void AssertAwsUrl(string url, string what)
    {
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"{what}: not an absolute URL - {url}");
        Assert.True(uri!.Scheme == Uri.UriSchemeHttps, $"{what}: not https - {url}");
        Assert.True(uri.Host.EndsWith("amazon.com", StringComparison.OrdinalIgnoreCase),
            $"{what}: not an AWS host - {url}");
    }

    [Fact]
    public void Slugs_are_unique_within_a_certification()
    {
        // Seeding upserts on slug, so a duplicate would silently make one topic overwrite another
        // instead of adding it.
        foreach (var (code, topics) in LessonCatalog.ByCertification)
        {
            var duplicates = topics
                .GroupBy(t => t.Slug, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.True(duplicates.Count == 0, $"{code} has duplicate slugs: {string.Join(", ", duplicates)}");
        }
    }

    [Fact]
    public void Slugs_are_url_safe()
    {
        // The slug is a path segment and also names the icon file in the client.
        foreach (var (code, topics) in LessonCatalog.ByCertification)
            foreach (var topic in topics)
                Assert.True(
                    System.Text.RegularExpressions.Regex.IsMatch(topic.Slug, "^[a-z0-9]+(-[a-z0-9]+)*$"),
                    $"{code}/{topic.Slug}: not a lowercase hyphenated slug");
    }

    [Fact]
    public void Every_topic_names_a_real_exam_domain()
    {
        // A typo here silently leaves DomainId null, which drops the topic out of the domain mix
        // the curriculum is balanced against.
        string[] domains =
        [
            "Cloud Concepts", "Security and Compliance",
            "Cloud Technology and Services", "Billing, Pricing, and Support",
        ];

        foreach (var (code, topics) in LessonCatalog.ByCertification)
            foreach (var topic in topics)
                Assert.True(domains.Contains(topic.Domain), $"{code}/{topic.Slug}: unknown domain '{topic.Domain}'");
    }
}
