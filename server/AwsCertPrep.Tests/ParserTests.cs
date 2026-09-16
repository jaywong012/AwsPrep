using AwsCertPrep.Api.Services;

namespace AwsCertPrep.Tests;

/// <summary>
/// The two parsers that sit between a provider's reply and the database.
///
/// The shared rule they both have to honour: a bad reply from a model is a retryable
/// <see cref="AiProviderException"/>, never a JsonException. Letting one escape is what turned a
/// truncated lesson into an HTTP 500 for the learner rather than a page that degrades.
/// </summary>
public class LessonJsonParserTests
{
    private const string Valid = """
        {
          "overview": "Amazon S3 stores objects in buckets and is reached over HTTP rather than mounted as a drive.",
          "useCases": ["Storing user uploads", "Keeping nightly backups"],
          "costNotes": "You pay per GB-month by storage class, plus requests and data transfer out to the internet.",
          "integrations": ["Amazon CloudFront", "AWS Lambda"],
          "realWorldExample": "An insurer moved scanned claim documents off a NAS appliance and into a bucket with lifecycle rules.",
          "examTraps": ["S3 versus EBS", "S3 versus EFS"]
        }
        """;

    [Fact]
    public void Parses_a_well_formed_lesson()
    {
        var lesson = LessonJsonParser.Parse(Valid);

        Assert.Contains("buckets", lesson.Overview);
        Assert.Equal(2, lesson.UseCases.Count);
        Assert.Equal(2, lesson.ExamTraps.Count);
    }

    [Fact]
    public void Parses_a_lesson_wrapped_in_a_markdown_fence()
    {
        var lesson = LessonJsonParser.Parse($"```json\n{Valid}\n```");

        Assert.Contains("buckets", lesson.Overview);
    }

    [Fact]
    public void Malformed_json_becomes_a_provider_error_not_a_json_exception()
    {
        // The regression: this used to surface as a 500. The caller only degrades the page for
        // AiProviderException, so the type here is the behaviour under test.
        Assert.Throws<AiProviderException>(
            () => LessonJsonParser.Parse("""{"overview": "unterminated, "useCases": [}"""));
    }

    [Fact]
    public void A_refusal_with_no_json_becomes_a_provider_error()
    {
        Assert.Throws<AiProviderException>(() => LessonJsonParser.Parse("I cannot help with that."));
    }

    [Fact]
    public void A_valid_lesson_passes_validation()
    {
        Assert.True(GeneratedLessonValidator.IsValid(LessonJsonParser.Parse(Valid), out _));
    }

    [Fact]
    public void A_truncated_overview_is_rejected_rather_than_cached()
    {
        // Notes are cached for the life of the topic, so accepting a stub would leave the learner
        // permanently looking at it.
        var lesson = LessonJsonParser.Parse(Valid);
        lesson.Overview = "Too short.";

        Assert.False(GeneratedLessonValidator.IsValid(lesson, out var reason));
        Assert.Contains("overview", reason);
    }

    [Fact]
    public void A_single_use_case_is_rejected()
    {
        var lesson = LessonJsonParser.Parse(Valid);
        lesson.UseCases = ["Only one"];

        Assert.False(GeneratedLessonValidator.IsValid(lesson, out var reason));
        Assert.Contains("use cases", reason);
    }

    [Fact]
    public void Bullet_markers_and_blank_entries_are_stripped()
    {
        var bullets = GeneratedLessonValidator.Bullets(["- First item", "* Second item", "  ", "x"]);

        Assert.Equal(["First item", "Second item"], bullets);
    }
}

public class JsonQuestionParserTests
{
    private const string OneQuestion = """
        {"stem":"Which service provides object storage?","options":[],"correctLabels":["A"]}
        """;

    [Fact]
    public void Accepts_a_bare_array()
    {
        var questions = JsonQuestionParser.Parse($"[{OneQuestion}]");

        Assert.Single(questions);
    }

    [Theory]
    [InlineData("questions")]
    [InlineData("items")]
    [InlineData("data")]
    [InlineData("results")]
    public void Accepts_any_of_the_wrapper_names_models_pick(string wrapper)
    {
        // Which wrapper key a model chooses is not stable between providers or even between calls,
        // so all four are accepted rather than prompting harder and hoping.
        var questions = JsonQuestionParser.Parse($$"""{"{{wrapper}}":[{{OneQuestion}}]}""");

        Assert.Single(questions);
    }

    [Fact]
    public void Accepts_a_single_question_object()
    {
        Assert.Single(JsonQuestionParser.Parse(OneQuestion));
    }

    [Fact]
    public void An_object_with_no_questions_is_a_provider_error()
    {
        Assert.Throws<AiProviderException>(() => JsonQuestionParser.Parse("""{"note":"none today"}"""));
    }

    [Fact]
    public void Malformed_json_becomes_a_provider_error()
    {
        Assert.Throws<AiProviderException>(() => JsonQuestionParser.Parse("""{"questions":[{"stem":}]}"""));
    }
}

public class TutorJsonParserTests
{
    [Fact]
    public void Parses_an_answer()
    {
        var answer = TutorJsonParser.Parse("""{"answer":"No, S3 is not a mounted drive."}""");

        Assert.Equal("No, S3 is not a mounted drive.", answer.Answer);
    }

    [Fact]
    public void Ignores_a_followUps_field_left_over_from_an_older_prompt()
    {
        // Follow-up suggestions were removed from the prompt and the response. A model that still
        // volunteers them must not break the answer.
        var answer = TutorJsonParser.Parse("""{"answer":"Yes.","followUps":["And then?"]}""");

        Assert.Equal("Yes.", answer.Answer);
    }

    [Fact]
    public void An_empty_answer_is_rejected()
    {
        Assert.Throws<AiProviderException>(() => TutorJsonParser.Parse("""{"answer":"   "}"""));
    }

    [Fact]
    public void Malformed_json_becomes_a_provider_error()
    {
        Assert.Throws<AiProviderException>(() => TutorJsonParser.Parse("""{"answer": }"""));
    }
}
