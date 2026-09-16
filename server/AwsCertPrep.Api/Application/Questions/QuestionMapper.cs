using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Dtos;

/// <summary>
/// Turns a stored question into the shape the client sees.
///
/// <paramref name="includeAnswers"/> is the whole reason this is one place rather than inlined at
/// each call site: a question sent mid-exam must not carry its own answer key, and a question
/// shown after scoring must. Getting that wrong in one handler would quietly leak the answers.
/// </summary>
public static class QuestionMapper
{
    public static QuestionDto ToDto(Question q, bool includeAnswers) => new(
        q.Id,
        q.Stem,
        q.Type,
        q.Difficulty,
        q.Source,
        q.Domain?.Name,
        q.ServiceTags,
        includeAnswers ? q.Explanation : null,
        q.Options
            .OrderBy(o => o.Label)
            .Select(o => new OptionDto(o.Label, o.Text, includeAnswers ? o.IsCorrect : null))
            .ToList());
}
