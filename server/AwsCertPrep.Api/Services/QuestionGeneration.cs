using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

public static class QuestionHasher
{
    /// <summary>Stable hash of the normalised stem, used to dedupe generated questions.</summary>
    public static string Hash(string stem)
    {
        var normalised = new string(stem.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)))[..32];
    }
}

/// <summary>Raw shape returned by the LLM before validation/persistence.</summary>
public class GeneratedQuestion
{
    [JsonPropertyName("stem")] public string Stem { get; set; } = "";
    [JsonPropertyName("options")] public List<GeneratedOption> Options { get; set; } = [];
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
    [JsonPropertyName("domain")] public string? Domain { get; set; }
    [JsonPropertyName("difficulty")] public string? Difficulty { get; set; }
    [JsonPropertyName("serviceTags")] public List<string> ServiceTags { get; set; } = [];
}

public class GeneratedOption
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("isCorrect")] public bool IsCorrect { get; set; }
}

public record GenerationContext(
    string CertificationCode,
    string CertificationName,
    CertificationLevel Level,
    string TargetCandidate,
    string[] OutOfScopeTasks,
    string[] DomainNames,
    string? TargetDomain,
    Difficulty Difficulty,
    int Count,
    string? TopicHint,
    IReadOnlyList<string> ExistingStems,
    // Real exam-style items from the reference bank, shown to the model as the calibration
    // target for voice, stem length and distractor construction.
    IReadOnlyList<ReferenceItem> Exemplars,
    // Blueprint topics the reference bank tests but the local bank barely covers, so a run with
    // no explicit topic hint still widens coverage instead of re-treading the same few services.
    IReadOnlyList<string> UndercoveredTopics);

public record GenerationResult(
    IReadOnlyList<GeneratedQuestion> Questions,
    string Provider,
    string Model,
    string? Warning);

public interface IQuestionGenerator
{
    string Provider { get; }
    Task<GenerationResult> GenerateAsync(GenerationContext ctx, CancellationToken ct);
}

public class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Gemini | Groq | DeepSeek | Offline</summary>
    public string Provider { get; set; } = "Offline";

    /// <summary>
    /// Fallback key, used when <see cref="Keys"/> has no entry for the active provider.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// A key per provider, so more than one can be configured at once and switching providers is
    /// just a change of <see cref="Provider"/>. Keyed by provider name ("Gemini", "DeepSeek"),
    /// case-insensitively. Keep these in user-secrets or a vault, never in appsettings.json.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The key for a provider: its own entry, then the shared one, then the environment.</summary>
    public string? KeyFor(string provider, string environmentVariable) =>
        Keys.TryGetValue(provider, out var key) && !string.IsNullOrWhiteSpace(key)
            ? key
            : !string.IsNullOrWhiteSpace(ApiKey)
                ? ApiKey
                : Environment.GetEnvironmentVariable(environmentVariable);
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 90;
}

public static class PromptBuilder
{
    public static string Build(GenerationContext ctx)
    {
        var maxWords = ExamItemRules.MaxStemWords(ctx.Level);
        var sb = new StringBuilder();

        sb.AppendLine($"You write items for the {ctx.CertificationCode} exam ({ctx.CertificationName}), an AWS Certified {ctx.Level} exam.");
        sb.AppendLine($"Write {ctx.Count} brand-new practice questions.");
        sb.AppendLine();

        sb.AppendLine("TARGET CANDIDATE (calibrate every item to exactly this person):");
        sb.AppendLine(ctx.TargetCandidate);
        sb.AppendLine();

        if (ctx.OutOfScopeTasks.Length > 0)
        {
            sb.AppendLine("OUT OF SCOPE. The exam guide states the target candidate is NOT expected to perform these");
            sb.AppendLine("tasks, so no item may require knowing how to do them:");
            foreach (var t in ctx.OutOfScopeTasks) sb.AppendLine($"- {t}");
            sb.AppendLine();
        }

        sb.AppendLine($"DIFFICULTY ({ctx.Difficulty}): {ExamItemRules.DifficultyGuidance(ctx.Level, ctx.Difficulty)}");
        sb.AppendLine("Difficulty never raises the certification tier. A Foundational item stays foundational.");
        sb.AppendLine();

        sb.AppendLine("EXAM DOMAINS:");
        foreach (var d in ctx.DomainNames) sb.AppendLine($"- {d}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ctx.TargetDomain))
            sb.AppendLine($"Every item MUST belong to this domain: {ctx.TargetDomain}");
        if (!string.IsNullOrWhiteSpace(ctx.TopicHint))
            sb.AppendLine($"Focus on this topic: {ctx.TopicHint}");

        // With no topic hint, steer towards blueprint topics the bank is thin on. Without this
        // the model keeps returning items about the same handful of headline services.
        if (string.IsNullOrWhiteSpace(ctx.TopicHint) && ctx.UndercoveredTopics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("COVERAGE GAP. The question bank already covers the popular services. These exam");
            sb.AppendLine("topics are barely covered, so spread the items across them:");
            sb.AppendLine(string.Join(", ", ctx.UndercoveredTopics));
        }

        sb.AppendLine();

        sb.AppendLine("ITEM FORMAT (the official AWS format - items breaking these rules are discarded):");
        sb.AppendLine($"1. Multiple choice: EXACTLY {ExamItemRules.MultipleChoiceOptionCount} options (A-D), exactly ONE correct.");
        sb.AppendLine($"2. Multiple response: EXACTLY {ExamItemRules.MultipleResponseMinOptions} options (A-E), exactly TWO correct, and the stem must end with \"(Select TWO.)\".");
        sb.AppendLine("3. Roughly one item in five is multiple response; the rest are multiple choice.");
        sb.AppendLine($"4. Keep the stem under {maxWords} words. {ExamItemRules.TypicalStemLength(ctx.Level)} Do not write every stem at the cap.");
        sb.AppendLine("5. Ask for ONE decision. Never stack three or more requirements or ask which option \"satisfies ALL\" of them.");
        sb.AppendLine();

        sb.AppendLine("WRITING RULES:");
        sb.AppendLine("6. Distractors are real AWS services or real concepts that a candidate with partial knowledge would plausibly pick. Never invent service names.");
        sb.AppendLine("6a. Use both stem shapes the real exam uses, in roughly this mix. About four items in ten "
                      + "open with one or two sentences of business need (\"A company needs to...\") and close with a "
                      + "single question sentence starting \"Which\", \"What\" or \"How\". The rest are a bare direct "
                      + "question of one sentence (\"Which AWS service...\") with no scenario at all. Do not force a "
                      + "scenario onto an item that is really a recall question.");
        sb.AppendLine("6b. When more than one option would work and the answer turns on a trade-off, capitalise the "
                      + "deciding qualifier: MOST cost-effective, LEAST operational overhead, MOST secure. Real exams "
                      + "do this in about one item in ten, so most items should carry no capitalised qualifier.");
        sb.AppendLine("6c. Keep options parallel in kind: either all service or feature names, or all short sentences. Never mix the two in one item.");
        sb.AppendLine("7. Every option must be definitively right or wrong. Never hedge with phrases like "
                      + string.Join(", ", ExamItemRules.BannedPhrases.Take(5).Select(p => $"\"{p}\"")) + ".");
        sb.AppendLine("8. No \"All of the above\" or \"None of the above\" options.");
        sb.AppendLine("9. Options must be similar in length and grammatical form, so length gives away nothing.");
        sb.AppendLine("10. The explanation says why the correct option is right AND why the strongest distractor is wrong.");
        sb.AppendLine("11. Use current AWS service names (Amazon Bedrock, Amazon SageMaker AI, Amazon Q).");
        sb.AppendLine("12. Set \"domain\" to exactly one of the domain names listed above.");
        sb.AppendLine("13. Do not repeat or paraphrase any existing question listed below.");

        if (ctx.Level == CertificationLevel.Foundational)
        {
            sb.AppendLine();
            sb.AppendLine("FOUNDATIONAL GUARDRAIL. Do not write items that turn on any of the following, they belong to");
            sb.AppendLine("higher-tier exams: hyperparameters, dropout, regularization, learning rates, epochs, batch sizes,");
            sb.AppendLine("gradient descent, activation functions, quantization, instance-type selection, VPC/subnet design,");
            sb.AppendLine("IAM policy JSON, or any code or template snippet.");
        }

        if (ctx.Exemplars.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"STYLE EXEMPLARS. Real {ctx.CertificationCode} items. They are the calibration target:");
            sb.AppendLine("match their stem length, their scenario framing, how plainly the options are worded, and");
            sb.AppendLine("how close the distractors sit to the key. Never reuse an exemplar's scenario or its answer.");

            var n = 0;
            foreach (var exemplar in ctx.Exemplars)
            {
                n++;
                var format = exemplar.IsMultipleResponse ? "multiple response" : "multiple choice";
                var domain = exemplar.DomainName is null ? "" : $", {exemplar.DomainName}";

                sb.AppendLine();
                sb.AppendLine($"Exemplar {n} ({format}{domain}):");
                sb.AppendLine($"Q: {exemplar.Stem}");
                foreach (var option in exemplar.Options)
                    sb.AppendLine($"   {option.Label}. {option.Text}{(option.IsCorrect ? "   <= correct" : "")}");

                // Only a substantive rationale is worth showing. A one-line source explanation
                // restates the key, and imitating that would undo writing rule 10.
                if (exemplar.Explanation.Length >= 80)
                    sb.AppendLine($"Why: {Truncate(exemplar.Explanation, 300)}");
            }
        }

        if (ctx.ExistingStems.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("EXISTING QUESTIONS TO AVOID. Do not write an item that tests the same decision as any of these,");
            sb.AppendLine("even with a different scenario:");
            foreach (var s in ctx.ExistingStems.Take(25))
                sb.AppendLine($"- {Truncate(s, 160)}");
        }

        sb.AppendLine();
        sb.AppendLine("SELF-CHECK before answering. Drop and replace any item where:");
        sb.AppendLine("- a second option could be defended as correct on the stated requirement;");
        sb.AppendLine("- the correct option is the longest or the only specific one, so it can be spotted without knowing AWS;");
        sb.AppendLine("- the explanation does not name the strongest distractor and say why it loses;");
        sb.AppendLine("- the item repeats an exemplar or an existing question above.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY JSON of this exact shape, with no markdown fences:");
        sb.AppendLine("""
        {"questions":[{"stem":"...","domain":"...","difficulty":"Easy|Medium|Hard","serviceTags":["Amazon Bedrock"],"explanation":"...","options":[{"label":"A","text":"...","isCorrect":true}]}]}
        """);
        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
}

/// <summary>
/// Enforces the official AWS item format and the certification's scope. Anything that fails
/// here is discarded rather than stored, so the bank cannot drift above its exam tier.
/// </summary>
public static class GeneratedQuestionValidator
{
    public static bool IsValid(GeneratedQuestion q, CertificationLevel level, out string reason)
    {
        reason = "";

        if (string.IsNullOrWhiteSpace(q.Stem) || q.Stem.Length < 20) { reason = "stem too short"; return false; }
        if (q.Stem.Length > 2000) { reason = "stem exceeds 2000 characters"; return false; }

        var stemWords = q.Stem.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var maxWords = ExamItemRules.MaxStemWords(level);
        if (stemWords > maxWords) { reason = $"stem is {stemWords} words, over the {maxWords}-word limit for {level}"; return false; }

        if (q.Options.Any(o => string.IsNullOrWhiteSpace(o.Text) || o.Text.Length > 1000)) { reason = "bad option text"; return false; }

        var labels = q.Options.Select(o => o.Label?.Trim().ToUpperInvariant() ?? "").ToList();
        if (labels.Any(string.IsNullOrEmpty) || labels.Distinct().Count() != labels.Count) { reason = "duplicate or missing option labels"; return false; }

        var texts = q.Options.Select(o => o.Text.Trim().ToLowerInvariant()).ToList();
        if (texts.Distinct().Count() != texts.Count) { reason = "duplicate options"; return false; }

        // Official format: MC is 1-of-4; MR is 2+ correct out of 5+ options.
        var correct = q.Options.Count(o => o.IsCorrect);
        if (correct == 0) { reason = "no correct option"; return false; }
        if (correct == q.Options.Count) { reason = "every option marked correct"; return false; }

        if (correct == 1)
        {
            if (q.Options.Count != ExamItemRules.MultipleChoiceOptionCount)
            {
                reason = $"multiple choice must have exactly {ExamItemRules.MultipleChoiceOptionCount} options, found {q.Options.Count}";
                return false;
            }
        }
        else if (level == CertificationLevel.Foundational)
        {
            // Every multiple-response item in the CLF-C02 reference bank is five options with
            // exactly two keyed. The prompt asks for that shape, so anything else is drift.
            if (q.Options.Count != ExamItemRules.FoundationalMultipleResponseOptionCount)
            {
                reason = $"multiple response on a {level} exam must have exactly {ExamItemRules.FoundationalMultipleResponseOptionCount} options, found {q.Options.Count}";
                return false;
            }
            if (correct != ExamItemRules.FoundationalMultipleResponseCorrectCount)
            {
                reason = $"multiple response on a {level} exam must have exactly {ExamItemRules.FoundationalMultipleResponseCorrectCount} correct options, found {correct}";
                return false;
            }
        }
        else
        {
            if (q.Options.Count < ExamItemRules.MultipleResponseMinOptions)
            {
                reason = $"multiple response needs at least {ExamItemRules.MultipleResponseMinOptions} options, found {q.Options.Count}";
                return false;
            }
            if (correct < ExamItemRules.MultipleResponseMinCorrect)
            {
                reason = $"multiple response needs at least {ExamItemRules.MultipleResponseMinCorrect} correct options";
                return false;
            }
        }

        // Every real multiple-response item announces itself in the stem, and no real
        // single-answer item does. Without the instruction the candidate cannot tell how many
        // options to pick, so a mismatch makes the item unanswerable rather than merely untidy.
        var announcesMultiple = q.Stem.Contains("(Select", StringComparison.OrdinalIgnoreCase)
            || q.Stem.Contains("Choose TWO", StringComparison.OrdinalIgnoreCase)
            || q.Stem.Contains("Choose THREE", StringComparison.OrdinalIgnoreCase);

        if (correct > 1 && !announcesMultiple) { reason = "multiple response stem does not end with \"(Select TWO.)\""; return false; }
        if (correct == 1 && announcesMultiple) { reason = "single-answer stem asks the candidate to select more than one"; return false; }

        if (string.IsNullOrWhiteSpace(q.Explanation) || q.Explanation.Length < 20) { reason = "missing explanation"; return false; }

        var haystack = $"{q.Stem} {string.Join(' ', q.Options.Select(o => o.Text))}".ToLowerInvariant();

        foreach (var phrase in ExamItemRules.BannedPhrases)
        {
            if (haystack.Contains(phrase, StringComparison.Ordinal))
            {
                reason = $"contains hedging or invalid phrase \"{phrase}\"";
                return false;
            }
        }

        // "Which option satisfies ALL of these requirements" stacks requirements, which the
        // real exams avoid; it is also the shape that pushes foundational items up a tier.
        if (haystack.Contains("satisfies all", StringComparison.Ordinal)
            || haystack.Contains("meets all of these", StringComparison.Ordinal)
            || haystack.Contains("all of these requirements", StringComparison.Ordinal))
        {
            reason = "stacks multiple requirements (\"satisfies ALL\")";
            return false;
        }

        foreach (var term in ExamItemRules.OutOfScopeTerms(level))
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
            {
                reason = $"out of scope for a {level} exam (mentions \"{term}\")";
                return false;
            }
        }

        return true;
    }
}
