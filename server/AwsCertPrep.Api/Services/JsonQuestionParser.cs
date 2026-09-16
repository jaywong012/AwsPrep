using System.Text.Json;
using System.Text.Json.Serialization;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Tolerant parser for LLM JSON output: strips markdown fences, accepts either
/// {"questions":[...]} or a bare [...] array, and ignores trailing prose.
/// </summary>
public static class JsonQuestionParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static List<GeneratedQuestion> Parse(string text)
    {
        var payload = LlmJson.Extract(text);

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
                return Deserialize(root.GetRawText());

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "questions", "items", "data", "results" })
                {
                    if (root.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        return Deserialize(arr.GetRawText());
                }

                // Single question object.
                if (root.TryGetProperty("stem", out _))
                {
                    var single = JsonSerializer.Deserialize<GeneratedQuestion>(root.GetRawText(), Options);
                    return single is null ? [] : [single];
                }
            }
        }
        catch (JsonException ex)
        {
            throw new AiProviderException($"Model returned invalid JSON: {ex.Message}");
        }

        throw new AiProviderException("Model response did not contain a questions array.");
    }

    private static List<GeneratedQuestion> Deserialize(string arrayJson) =>
        JsonSerializer.Deserialize<List<GeneratedQuestion>>(arrayJson, Options) ?? [];
}
