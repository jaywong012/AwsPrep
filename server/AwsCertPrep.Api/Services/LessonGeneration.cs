using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

/// <summary>Raw shape returned by the LLM for one lesson, before validation and persistence.</summary>
public class GeneratedLesson
{
    [JsonPropertyName("overview")] public string Overview { get; set; } = "";
    [JsonPropertyName("useCases")] public List<string> UseCases { get; set; } = [];
    [JsonPropertyName("costNotes")] public string CostNotes { get; set; } = "";
    [JsonPropertyName("integrations")] public List<string> Integrations { get; set; } = [];
    [JsonPropertyName("realWorldExample")] public string RealWorldExample { get; set; } = "";
    [JsonPropertyName("examTraps")] public List<string> ExamTraps { get; set; } = [];
}

/// <summary>
/// Builds the prompt for one lesson.
///
/// The seeded facts are passed in as given truth the model must write around rather than restate
/// or contradict: the learner already sees the seeded purpose and pricing model above the
/// generated text, so a model that invents a different answer is visibly wrong on the same screen.
/// The exam guide's domain and the certification's target candidate keep the depth at the right
/// tier - a Cloud Practitioner lesson must not turn into an architect's design discussion.
/// </summary>
public static class LessonPromptBuilder
{
    public const string SystemPrompt =
        "You are an AWS instructor writing study notes for a certification candidate. "
        + "You are accurate above all else: you never invent service names, limits, or prices. "
        + "You always reply with valid JSON only.";

    public static string Build(LessonTopic topic, Certification cert, IReadOnlyList<string> relatedTopicTitles)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Write study notes on \"{topic.Title}\" for the {cert.Code} exam ({cert.Name}),");
        sb.AppendLine($"an AWS Certified {cert.Level} exam.");
        sb.AppendLine();

        sb.AppendLine("TARGET READER (pitch every sentence at exactly this person):");
        sb.AppendLine(cert.TargetCandidate);
        sb.AppendLine();

        sb.AppendLine("ESTABLISHED FACTS. These are verified and already shown to the learner above your text.");
        sb.AppendLine("Write around them. Never contradict them and never simply repeat them:");
        sb.AppendLine($"- Topic: {topic.Title}");
        sb.AppendLine($"- Exam domain: {topic.Domain?.Name ?? "General"}");
        sb.AppendLine($"- Category: {topic.Category}");
        sb.AppendLine($"- What it is for: {topic.Purpose}");
        sb.AppendLine($"- How it is charged: {topic.PricingModel}");
        sb.AppendLine($"- Official documentation: {topic.DocsUrl}");
        if (!string.IsNullOrWhiteSpace(topic.PricingUrl))
            sb.AppendLine($"- Official pricing page: {topic.PricingUrl}");
        sb.AppendLine();

        if (relatedTopicTitles.Count > 0)
        {
            sb.AppendLine("OTHER TOPICS IN THIS CURRICULUM. Prefer these when naming a related or confused service,");
            sb.AppendLine("so the notes cross-reference the syllabus the learner is actually studying:");
            sb.AppendLine(string.Join("; ", relatedTopicTitles));
            sb.AppendLine();
        }

        sb.AppendLine("SECTIONS TO WRITE:");
        sb.AppendLine("1. overview: 2-3 sentences expanding what this is and the problem it solves. Plain English, no marketing.");
        sb.AppendLine("2. useCases: 3-5 bullets. Each is a situation an exam question would describe, phrased as the situation,");
        sb.AppendLine("   not as the service name. Write \"A team needs shared storage several EC2 instances can mount at once\",");
        sb.AppendLine("   not \"Use it for shared storage\".");
        sb.AppendLine("3. costNotes: 2-4 sentences on what actually drives the bill, what is free, and the one cost mistake");
        sb.AppendLine("   people make with this topic. Never quote a specific dollar figure - prices change and the learner");
        sb.AppendLine("   has the official pricing page. Describe the shape of the charge instead.");
        sb.AppendLine("4. integrations: 3-5 bullets, each \"Other AWS service - why the two are used together\".");
        sb.AppendLine("   Only real AWS services, named in full and current form.");
        sb.AppendLine("5. realWorldExample: one concrete worked scenario of 3-5 sentences. Give the company a shape");
        sb.AppendLine("   (size, industry, what they were doing before), say what they built, and say what it got them.");
        sb.AppendLine("6. examTraps: 3-5 bullets. Each names the service or concept this is confused with on the exam and");
        sb.AppendLine("   states the single line that separates them. This is the most valuable section - be specific.");
        sb.AppendLine("   Example shape: \"Amazon EBS vs Amazon EFS - EBS attaches to one instance in one AZ; EFS is mounted");
        sb.AppendLine("   by many instances across AZs.\"");
        sb.AppendLine();

        sb.AppendLine("RULES:");
        sb.AppendLine("- Accuracy beats completeness. If you are unsure of a detail, leave it out.");
        sb.AppendLine("- Never invent a service, feature, limit or price.");
        sb.AppendLine("- Use current AWS names (Amazon SageMaker AI, Amazon Bedrock, Amazon Q).");
        sb.AppendLine("- No headings, markdown or bullet characters inside the strings - the app renders the structure.");
        sb.AppendLine($"- Stay at {cert.Level} depth. Do not drift into architecture design, code, or instance-type selection.");
        sb.AppendLine();

        sb.AppendLine("Return ONLY JSON of this exact shape, with no markdown fences:");
        sb.AppendLine("""
        {"overview":"...","useCases":["..."],"costNotes":"...","integrations":["Amazon CloudFront - ..."],"realWorldExample":"...","examTraps":["Amazon EBS vs Amazon EFS - ..."]}
        """);

        return sb.ToString();
    }
}

/// <summary>
/// Parses the model's lesson JSON. Tolerant of the fences and leading prose models add despite
/// being told not to, for the same reason <see cref="JsonQuestionParser"/> is.
/// </summary>
public static class LessonJsonParser
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static GeneratedLesson Parse(string text)
    {
        var json = LlmJson.Extract(text);

        try
        {
            return JsonSerializer.Deserialize<GeneratedLesson>(json, Options)
                   ?? throw new AiProviderException("The model returned no lesson content.");
        }
        catch (JsonException ex)
        {
            // Deliberately re-thrown as AiProviderException. The caller only degrades the page
            // for that type, so letting a JsonException escape turned a retryable bad response
            // from the model into a 500 for the learner.
            throw new AiProviderException($"The model returned invalid JSON for this lesson: {ex.Message}");
        }
    }
}

/// <summary>
/// Rejects a lesson that is too thin to be worth caching for the life of the topic. A short or
/// empty section is the common failure when a provider truncates, and caching it would leave the
/// learner permanently looking at a stub.
/// </summary>
public static class GeneratedLessonValidator
{
    private const int MinProseLength = 80;
    private const int MinBullets = 2;

    public static bool IsValid(GeneratedLesson lesson, out string reason)
    {
        reason = "";

        if (Clean(lesson.Overview).Length < MinProseLength) { reason = "overview too short"; return false; }
        if (Clean(lesson.CostNotes).Length < MinProseLength) { reason = "cost notes too short"; return false; }
        if (Clean(lesson.RealWorldExample).Length < MinProseLength) { reason = "real-world example too short"; return false; }

        if (Bullets(lesson.UseCases).Count < MinBullets) { reason = "needs at least two use cases"; return false; }
        if (Bullets(lesson.Integrations).Count < MinBullets) { reason = "needs at least two integrations"; return false; }
        if (Bullets(lesson.ExamTraps).Count < MinBullets) { reason = "needs at least two exam traps"; return false; }

        return true;
    }

    /// <summary>Bullets survive as newline-separated text, which is how LessonContent stores them.</summary>
    public static List<string> Bullets(IEnumerable<string> values) =>
        values
            .Select(v => Clean(v).TrimStart('-', '*', '•', ' '))
            .Where(v => v.Length > 3)
            .ToList();

    public static string Clean(string? value) => (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
}
