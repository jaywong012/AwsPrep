namespace AwsCertPrep.Api.Services;

/// <summary>
/// Generates questions through whichever provider <see cref="IAiTextCompletion"/> is bound to.
/// The prompt, the parsing and the validation are provider-independent, so this is the whole of
/// the provider-specific question path.
/// </summary>
public class AiQuestionGenerator(IAiTextCompletion completion) : IQuestionGenerator
{
    private const string SystemPrompt =
        "You are an expert AWS certification exam item writer. You always reply with valid JSON only.";

    public string Provider => completion.Provider;

    public async Task<GenerationResult> GenerateAsync(GenerationContext ctx, CancellationToken ct)
    {
        var text = await completion.CompleteJsonAsync(SystemPrompt, PromptBuilder.Build(ctx), ct);
        return new GenerationResult(JsonQuestionParser.Parse(text), Provider, completion.Model, null);
    }
}
