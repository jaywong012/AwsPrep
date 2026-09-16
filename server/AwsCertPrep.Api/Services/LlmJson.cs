namespace AwsCertPrep.Api.Services;

/// <summary>
/// Pulls the JSON payload out of whatever a model actually returned.
///
/// Both parsers need this and both originally did it by taking everything between the first
/// opening brace and the <em>last</em> closing brace. That works until a response is truncated
/// mid-array, because the last brace in the text is then one inside a string or a nested object,
/// and the slice comes back unbalanced - which is how a model that ran out of output tokens
/// turned into a 500 rather than a retryable "the model returned bad JSON".
///
/// This scans forward from the first opening bracket and stops at the point where nesting returns
/// to zero, tracking string state so braces inside text are ignored. If nesting never closes, the
/// response really was truncated and we say so.
/// </summary>
public static class LlmJson
{
    /// <summary>
    /// The first complete JSON object or array in <paramref name="text"/>, with markdown fences
    /// and any prose around it removed.
    /// </summary>
    /// <exception cref="AiProviderException">No JSON present, or it is truncated.</exception>
    public static string Extract(string text)
    {
        var t = StripFence(text);

        var start = FirstBracket(t);
        if (start < 0) throw new AiProviderException("The model returned no JSON.");

        var end = MatchingClose(t, start);
        if (end < 0)
            throw new AiProviderException(
                "The model's JSON was cut off before it closed - it probably ran out of output tokens.");

        return t[start..(end + 1)];
    }

    private static string StripFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;

        // Drop the opening fence line (which may carry a language tag) and the closing fence.
        var firstNewline = t.IndexOf('\n');
        if (firstNewline > 0) t = t[(firstNewline + 1)..];

        var fenceEnd = t.LastIndexOf("```", StringComparison.Ordinal);
        if (fenceEnd >= 0) t = t[..fenceEnd];

        return t.Trim();
    }

    private static int FirstBracket(string t)
    {
        for (var i = 0; i < t.Length; i++)
            if (t[i] is '{' or '[')
                return i;

        return -1;
    }

    /// <summary>
    /// Index of the bracket that closes the one at <paramref name="start"/>, or -1 if the text
    /// ends first. Characters inside string literals are skipped, and a backslash escapes the
    /// next character, so a brace or quote in the prose cannot unbalance the count.
    /// </summary>
    private static int MatchingClose(string t, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < t.Length; i++)
        {
            var c = t[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c is '{' or '[') depth++;
            else if (c is '}' or ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }
}
