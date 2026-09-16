using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// One JSON-returning call to whichever LLM provider is configured.
///
/// Question generation and lesson generation both need exactly this and nothing more, so the
/// provider plumbing - key handling, retries, error translation - lives here once rather than
/// being copied per feature. Adding a provider means adding one implementation of this interface.
/// </summary>
public interface IAiTextCompletion
{
    /// <summary>Gemini | Groq | Offline. Surfaced to the client so it can show what wrote an item.</summary>
    string Provider { get; }

    string Model { get; }

    /// <summary>
    /// False when no provider is set up. Callers use this to degrade gracefully - the lessons tab
    /// still serves its seeded facts - instead of throwing on a path the learner did not ask for.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>Returns the raw JSON text the model produced. Throws on a provider failure.</summary>
    Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

/// <summary>
/// Google Gemini. The Gemini API has a free tier - create a key at
/// https://aistudio.google.com/apikey and set Ai:ApiKey (or GEMINI_API_KEY).
/// </summary>
public class GeminiTextCompletion(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<GeminiTextCompletion> logger) : IAiTextCompletion
{
    private readonly AiOptions _opts = options.Value;

    public string Provider => "Gemini";

    // "gemini-flash-latest" tracks the current free-tier flash model, so the app keeps
    // working when Google retires a specific version. Override with Ai:Model to pin one.
    public string Model => string.IsNullOrWhiteSpace(_opts.Model) ? "gemini-flash-latest" : _opts.Model!;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private string? ApiKey => _opts.KeyFor(Provider, "GEMINI_API_KEY");

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiConfigurationException("Gemini API key missing. Set Ai:ApiKey in appsettings or the GEMINI_API_KEY environment variable.");

        var body = new
        {
            systemInstruction = new { parts = new[] { new { text = systemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = userPrompt } } }
            },
            generationConfig = new
            {
                temperature = 0.9,
                responseMimeType = "application/json"
            }
        };

        var url = $"v1beta/models/{Model}:generateContent";
        var (response, raw) = await TransientRetry.SendAsync(
            token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
                request.Headers.Add("x-goog-api-key", apiKey);
                return http.SendAsync(request, token);
            },
            logger,
            ct);

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Gemini call failed ({Status}): {Body}", (int)response.StatusCode, Summarise(raw));
            throw new AiProviderException(AiProviderErrors.Describe(Provider, response.StatusCode, Model));
        }

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(raw);
        var text = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException("Gemini returned an empty response.");

        return text;
    }

    internal static string Summarise(string raw) => raw.Length <= 300 ? raw : raw[..300] + "...";

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")] public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonPropertyName("parts")] public List<Part>? Parts { get; set; }
    }

    private class Part
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}

/// <summary>
/// Groq (OpenAI-compatible chat completions). Groq offers a free developer tier -
/// create a key at https://console.groq.com/keys and set Ai:ApiKey (or GROQ_API_KEY).
/// </summary>
public class GroqTextCompletion(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<GroqTextCompletion> logger) : IAiTextCompletion
{
    private readonly AiOptions _opts = options.Value;

    public string Provider => "Groq";

    public string Model => string.IsNullOrWhiteSpace(_opts.Model) ? "llama-3.3-70b-versatile" : _opts.Model!;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private string? ApiKey => _opts.KeyFor(Provider, "GROQ_API_KEY");

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiConfigurationException("Groq API key missing. Set Ai:ApiKey in appsettings or the GROQ_API_KEY environment variable.");

        var body = new
        {
            model = Model,
            temperature = 0.9,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var (response, raw) = await TransientRetry.SendAsync(
            token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "openai/v1/chat/completions")
                {
                    Content = JsonContent.Create(body)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return http.SendAsync(request, token);
            },
            logger,
            ct);

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Groq call failed ({Status}): {Body}", (int)response.StatusCode, GeminiTextCompletion.Summarise(raw));
            throw new AiProviderException(AiProviderErrors.Describe(Provider, response.StatusCode, Model));
        }

        var parsed = JsonSerializer.Deserialize<GroqResponse>(raw);
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException("Groq returned an empty response.");

        return text;
    }

    private class GroqResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}

/// <summary>
/// DeepSeek (OpenAI-compatible chat completions). Paid, but cheap: create a key at
/// https://platform.deepseek.com and set Ai:ApiKey (or DEEPSEEK_API_KEY).
///
/// Shares the OpenAI request shape with <see cref="GroqTextCompletion"/> but is kept separate
/// rather than parameterised: the two differ in base URL, path, default model and the errors they
/// return, and one class with three provider switches inside it reads worse than two small ones.
/// </summary>
public class DeepSeekTextCompletion(
    HttpClient http,
    IOptions<AiOptions> options,
    ILogger<DeepSeekTextCompletion> logger) : IAiTextCompletion
{
    private readonly AiOptions _opts = options.Value;

    public string Provider => "DeepSeek";

    /// <summary>"deepseek-flash" is the cheap fast tier; "deepseek-v4-pro" is the strong one.</summary>
    public string Model => string.IsNullOrWhiteSpace(_opts.Model) ? "deepseek-flash" : _opts.Model!;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    private string? ApiKey => _opts.KeyFor(Provider, "DEEPSEEK_API_KEY");

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var apiKey = ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiConfigurationException("DeepSeek API key missing. Set Ai:ApiKey in appsettings or the DEEPSEEK_API_KEY environment variable.");

        var body = new
        {
            model = Model,
            temperature = 0.9,
            // DeepSeek's JSON mode requires the word "json" somewhere in the prompt; both the
            // question and the lesson prompts end with "Return ONLY JSON", so that holds.
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var (response, raw) = await TransientRetry.SendAsync(
            token =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                {
                    Content = JsonContent.Create(body)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                return http.SendAsync(request, token);
            },
            logger,
            ct);

        using var _ = response;

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("DeepSeek call failed ({Status}): {Body}", (int)response.StatusCode, GeminiTextCompletion.Summarise(raw));
            throw new AiProviderException(AiProviderErrors.Describe(Provider, response.StatusCode, Model));
        }

        var parsed = JsonSerializer.Deserialize<DeepSeekResponse>(raw);
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException("DeepSeek returned an empty response.");

        return text;
    }

    private class DeepSeekResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}

/// <summary>
/// The no-provider stand-in. It never pretends to generate: <see cref="IsConfigured"/> is false so
/// callers can serve what they have without a provider, and calling it says plainly what to set.
/// </summary>
public class OfflineTextCompletion : IAiTextCompletion
{
    public string Provider => "Offline";
    public string Model => "none";
    public bool IsConfigured => false;

    public Task<string> CompleteJsonAsync(string systemPrompt, string userPrompt, CancellationToken ct) =>
        throw new AiConfigurationException(
            "No AI provider is configured. Set Ai:Provider to Gemini or Groq, with Ai:ApiKey, to generate content.");
}

/// <summary>No provider key or an unusable provider setting. The operator has to fix something.</summary>
public class AiConfigurationException(string message) : Exception(message);

/// <summary>The provider was reached but refused, failed, or returned something unusable.</summary>
public class AiProviderException(string message) : Exception(message);
