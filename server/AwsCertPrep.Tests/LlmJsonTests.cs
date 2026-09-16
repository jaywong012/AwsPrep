using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// Extraction of the JSON payload from whatever a model actually returned.
///
/// These are regression tests for a real incident: roughly 30% of lesson generations returned
/// HTTP 500 because the extractor took everything between the first '{' and the *last* '}'. When
/// a response was truncated mid-array the last brace in the text belonged to a nested object, so
/// the slice came back unbalanced and the JsonException escaped as a 500 instead of a retryable
/// "the model returned bad JSON". The truncation cases below are the ones that failed.
/// </summary>
public class LlmJsonTests
{
    [Fact]
    public void Extracts_a_bare_object()
    {
        Assert.Equal("""{"answer":"yes"}""", LlmJson.Extract("""{"answer":"yes"}"""));
    }

    [Fact]
    public void Strips_a_markdown_fence_with_a_language_tag()
    {
        var raw = "```json\n{\"answer\":\"yes\"}\n```";

        Assert.Equal("""{"answer":"yes"}""", LlmJson.Extract(raw));
    }

    [Fact]
    public void Ignores_prose_before_and_after_the_payload()
    {
        var raw = """
            Sure! Here is the JSON you asked for:
            {"answer":"yes"}
            Let me know if you need anything else.
            """;

        Assert.Equal("""{"answer":"yes"}""", LlmJson.Extract(raw));
    }

    [Fact]
    public void Stops_at_the_close_of_the_first_object_not_the_last_brace_in_the_text()
    {
        // The trailing prose contains a brace. Taking everything up to the last '}' would swallow
        // it and produce something that is not valid JSON.
        var raw = """{"answer":"yes"} — note that a policy looks like {"Effect":"Allow"} too.""";

        Assert.Equal("""{"answer":"yes"}""", LlmJson.Extract(raw));
    }

    [Fact]
    public void Keeps_braces_that_are_inside_string_values()
    {
        var raw = """{"answer":"a policy looks like {\"Effect\":\"Allow\"}","ok":true}""";

        Assert.Equal(raw, LlmJson.Extract(raw));
    }

    [Fact]
    public void Keeps_an_escaped_quote_from_ending_the_string_early()
    {
        var raw = """{"answer":"he said \"use S3\" and left"}""";

        Assert.Equal(raw, LlmJson.Extract(raw));
    }

    [Fact]
    public void Extracts_a_bare_array()
    {
        var raw = """[{"stem":"one"},{"stem":"two"}]""";

        Assert.Equal(raw, LlmJson.Extract(raw));
    }

    [Fact]
    public void Truncated_output_is_reported_as_truncated_rather_than_sliced()
    {
        // What a provider returns when it runs out of output tokens mid-array. The old first-to-last
        // brace slice produced unbalanced JSON here and surfaced to the learner as a 500.
        var raw = """{"useCases":["first case","second case","third ca""";

        var ex = Assert.Throws<AiProviderException>(() => LlmJson.Extract(raw));
        Assert.Contains("cut off", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Truncation_after_a_complete_nested_object_is_still_truncation()
    {
        // The nested object closes, so a last-brace slice would have looked balanced and produced
        // JSON that parses but silently loses every item after the first.
        var raw = """{"questions":[{"stem":"one","answer":"A"},{"stem":"two""";

        Assert.Throws<AiProviderException>(() => LlmJson.Extract(raw));
    }

    [Fact]
    public void Text_with_no_json_at_all_is_reported_as_such()
    {
        var ex = Assert.Throws<AiProviderException>(
            () => LlmJson.Extract("I'm sorry, I can't help with that request."));

        Assert.Contains("no JSON", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Empty_input_does_not_throw_an_index_error()
    {
        Assert.Throws<AiProviderException>(() => LlmJson.Extract(""));
    }
}
