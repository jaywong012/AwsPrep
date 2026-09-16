using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

public record ReferenceOption(string Label, string Text, bool IsCorrect);

/// <summary>
/// One real exam-style item from a reference bank, classified against the certification's
/// official domains. These items are the ground truth the app calibrates against: they seed
/// the question bank and they are shown to the LLM as style exemplars when it writes new items.
/// </summary>
public record ReferenceItem(
    string CertificationCode,
    int Number,
    string Stem,
    IReadOnlyList<ReferenceOption> Options,
    string Explanation,
    IReadOnlyList<string> References,
    string? DomainName,
    Difficulty Difficulty,
    IReadOnlyList<string> ServiceTags)
{
    public IEnumerable<ReferenceOption> CorrectOptions => Options.Where(o => o.IsCorrect);
    public bool IsMultipleResponse => Options.Count(o => o.IsCorrect) > 1;
    public string CorrectLabels => string.Join(", ", CorrectOptions.Select(o => o.Label));
}

/// <summary>
/// Loads the reference banks embedded under Data/ReferenceBank and classifies every item by
/// domain, difficulty and topic. Registered as a singleton: the files never change at runtime.
/// </summary>
public class ReferenceBank
{
    private const string ResourcePrefix = "AwsCertPrep.Api.Data.ReferenceBank.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, List<ReferenceItem>> _byCertification =
        new(StringComparer.OrdinalIgnoreCase);

    public ReferenceBank(ILogger<ReferenceBank>? logger = null)
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)))
        {
            var code = resource[ResourcePrefix.Length..].Replace(".json", "", StringComparison.OrdinalIgnoreCase);

            using var stream = assembly.GetManifestResourceStream(resource)!;
            var raw = JsonSerializer.Deserialize<List<ReferenceItemJson>>(stream, JsonOptions) ?? [];

            var items = raw
                .Where(r => r.Options.Count >= 4 && r.Correct.Count > 0 && r.Correct.Count < r.Options.Count)
                .Select(r => Build(code, r))
                .ToList();

            _byCertification[code] = items;
            logger?.LogInformation("Loaded {Count} reference items for {Code}.", items.Count, code);
        }
    }

    public IReadOnlyList<ReferenceItem> For(string certificationCode) =>
        _byCertification.TryGetValue(certificationCode, out var items) ? items : [];

    public bool Has(string certificationCode) => For(certificationCode).Count > 0;

    public IReadOnlyCollection<string> CertificationCodes => _byCertification.Keys;

    /// <summary>
    /// Style exemplars for a generation run: real items the model should imitate in voice,
    /// length and distractor construction. Items from the requested domain come first, and the
    /// selection is randomised so repeated runs do not converge on the same few items.
    /// One multiple-response exemplar is included when the bank has one, because that format is
    /// the one models most often get wrong.
    /// </summary>
    public IReadOnlyList<ReferenceItem> Exemplars(
        string certificationCode, string? domainName, int count = 3, Random? random = null)
    {
        var pool = For(certificationCode);
        if (pool.Count == 0 || count <= 0) return [];

        random ??= Random.Shared;

        var scoped = string.IsNullOrWhiteSpace(domainName)
            ? []
            : pool.Where(i => i.DomainName == domainName).ToList();
        var preferred = scoped.Count >= 2 ? scoped : pool;

        var chosen = new List<ReferenceItem>();

        var multipleResponse = preferred.Where(i => i.IsMultipleResponse).ToList();
        if (multipleResponse.Count > 0 && count > 1)
            chosen.Add(multipleResponse[random.Next(multipleResponse.Count)]);

        foreach (var item in preferred.Where(i => !i.IsMultipleResponse).OrderBy(_ => random.Next()))
        {
            if (chosen.Count >= count) break;
            chosen.Add(item);
        }

        return chosen;
    }

    /// <summary>
    /// Every topic the reference bank tests for a certification, most-tested first. Used to
    /// steer generation towards blueprint topics the user's own bank is still thin on.
    /// </summary>
    public IReadOnlyList<string> Topics(string certificationCode) =>
        For(certificationCode)
            .SelectMany(i => i.ServiceTags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .ToList();

    private static ReferenceItem Build(string code, ReferenceItemJson raw)
    {
        var options = raw.Options
            .Select(o => new ReferenceOption(o.Label, o.Text, raw.Correct.Contains(o.Label)))
            .ToList();

        var keyed = string.Join(" ", options.Where(o => o.IsCorrect).Select(o => o.Text));

        return new ReferenceItem(
            code,
            raw.Number,
            raw.Stem,
            options,
            BuildExplanation(raw, options),
            raw.References,
            DomainClassifier.Classify(code, raw.Stem, keyed),
            QuestionDifficultyRater.Rate(raw.Stem, options.Count(op => op.IsCorrect)),
            AwsServiceVocabulary.Extract($"{raw.Stem} {keyed}"));
    }

    /// <summary>
    /// Some source explanations only restate the key. Prefixing those keeps the text readable
    /// as an answer rather than as a truncated sentence, and appends the authoritative link.
    /// </summary>
    private static string BuildExplanation(ReferenceItemJson raw, List<ReferenceOption> options)
    {
        var text = raw.Explanation.Trim();

        if (text.Length < 40)
        {
            var keys = string.Join("; ", options.Where(o => o.IsCorrect).Select(o => o.Text));

            // A short source explanation is usually the key restated; keeping it would read as
            // "Correct answer: AWS Secrets Manager. AWS Secrets Manager."
            var restatesKey = text.Length == 0
                || keys.Contains(text.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

            text = restatesKey ? $"Correct answer: {keys}." : $"Correct answer: {keys}. {text}";
        }

        var reference = raw.References.FirstOrDefault();
        return reference is null ? text : $"{text} Reference: {reference}";
    }

    private class ReferenceItemJson
    {
        [JsonPropertyName("number")] public int Number { get; set; }
        [JsonPropertyName("stem")] public string Stem { get; set; } = "";
        [JsonPropertyName("options")] public List<OptionJson> Options { get; set; } = [];
        [JsonPropertyName("correct")] public List<string> Correct { get; set; } = [];
        [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
        [JsonPropertyName("references")] public List<string> References { get; set; } = [];
    }

    private class OptionJson
    {
        [JsonPropertyName("label")] public string Label { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
    }
}
