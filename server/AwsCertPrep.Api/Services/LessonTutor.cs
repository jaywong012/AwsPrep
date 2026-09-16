using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

/// <summary>One turn of the conversation, as sent back by the client.</summary>
public record TutorTurn(string Question, string Answer);

/// <summary>Raw shape the model returns for an answer.</summary>
public class GeneratedAnswer
{
    [JsonPropertyName("answer")] public string Answer { get; set; } = "";
}

/// <summary>
/// Answers a learner's question about the lesson they are currently reading.
///
/// The whole point is that the question arrives with its context already attached: the model is
/// handed the topic's verified facts and its generated notes, so "why would I use this instead of
/// EFS?" needs no explanation of what "this" is. It is also told to answer at the certification's
/// level, which stops a Cloud Practitioner question being answered like an architect's.
///
/// Stateless. The client sends back the recent turns it wants remembered, which keeps a
/// conversation working without a table to store and expire.
/// </summary>
public static class LessonTutorPrompt
{
    public const string SystemPrompt =
        "You are a patient AWS certification tutor helping someone study for a specific exam. "
        + "You are accurate above all else: you never invent services, limits or prices, and you say "
        + "plainly when something is outside what you can confirm. You always reply with valid JSON only.";

    public static string Build(
        LessonTopic topic,
        Certification cert,
        IReadOnlyList<TutorTurn> history,
        string question,
        TutorOptions options)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"A candidate studying for {cert.Code} ({cert.Name}), an AWS Certified {cert.Level} exam,");
        sb.AppendLine($"is reading the lesson on \"{topic.Title}\" and has asked you a question about it.");
        sb.AppendLine();

        sb.AppendLine("WHO THEY ARE. Pitch the answer at exactly this person:");
        sb.AppendLine(cert.TargetCandidate);
        sb.AppendLine();

        sb.AppendLine("THE LESSON THEY ARE READING.");
        sb.AppendLine($"Topic: {topic.Title}");
        sb.AppendLine($"Exam domain: {topic.Domain?.Name ?? "General"}");
        sb.AppendLine($"What it is for: {topic.Purpose}");
        sb.AppendLine($"How it is charged: {topic.PricingModel}");
        sb.AppendLine($"Official documentation: {topic.DocsUrl}");

        if (topic.Content is { } notes)
        {
            sb.AppendLine();
            sb.AppendLine("The notes on the page say:");
            sb.AppendLine($"Overview: {notes.Overview}");
            Section(sb, "When you would use it", notes.UseCases);
            sb.AppendLine($"Cost in practice: {notes.CostNotes}");
            Section(sb, "Works with", notes.Integrations);
            sb.AppendLine($"Worked example: {notes.RealWorldExample}");
            Section(sb, "Exam traps", notes.ExamTraps);
        }

        sb.AppendLine();

        if (history.Count > 0)
        {
            sb.AppendLine("THE CONVERSATION SO FAR:");
            foreach (var turn in history.TakeLast(options.MaxHistoryTurns))
            {
                sb.AppendLine($"Q: {Trim(turn.Question, options.MaxQuestionLength)}");
                sb.AppendLine($"A: {Trim(turn.Answer, options.MaxRepeatedAnswerLength)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("THEIR QUESTION:");
        sb.AppendLine(Trim(question, options.MaxQuestionLength));
        sb.AppendLine();

        sb.AppendLine("HOW TO ANSWER:");
        sb.AppendLine("- Answer the question directly in the first sentence, then explain.");
        sb.AppendLine("- 3 to 6 sentences. This is a study aid, not an essay.");
        sb.AppendLine($"- Stay at {cert.Level} depth. Do not drift into architecture design, code, IAM policy JSON or instance types.");
        sb.AppendLine("- Never contradict the lesson facts above. They are verified; if you believe one is wrong, say so explicitly rather than quietly answering differently.");
        sb.AppendLine("- Never quote a specific dollar figure. Describe the shape of the charge and point at the official pricing page.");
        sb.AppendLine("- When the answer turns on a distinction the exam tests, name both sides of it.");
        sb.AppendLine("- If the question is off-topic for this lesson, answer briefly and say which lesson covers it properly.");
        sb.AppendLine("- If you cannot confirm something, say so. A candidate acting on a confident wrong answer is the worst outcome here.");
        sb.AppendLine();

        sb.AppendLine("Return ONLY JSON of this exact shape, with no markdown fences:");
        sb.AppendLine("""{"answer":"..."}""");

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string label, string newlineSeparated)
    {
        var items = newlineSeparated.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) return;

        sb.AppendLine($"{label}: {string.Join(" | ", items)}");
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}

/// <summary>Parses the tutor's reply, tolerating the fences and prose models add anyway.</summary>
public static class TutorJsonParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static GeneratedAnswer Parse(string text)
    {
        var json = LlmJson.Extract(text);

        try
        {
            var parsed = JsonSerializer.Deserialize<GeneratedAnswer>(json, Options);

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Answer))
                throw new AiProviderException("The tutor returned an empty answer.");

            return parsed;
        }
        catch (JsonException ex)
        {
            throw new AiProviderException($"The tutor returned invalid JSON: {ex.Message}");
        }
    }
}
