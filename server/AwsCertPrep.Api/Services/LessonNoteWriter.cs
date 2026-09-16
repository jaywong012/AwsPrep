using AwsCertPrep.Api.Application.Options;
using AwsCertPrep.Api.Data;
using AwsCertPrep.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Writes the generated half of a lesson and caches it.
///
/// Separate from the lesson read model on purpose: reading a lesson is a cheap database
/// query that any number of learners can do, while writing one costs a provider call and
/// overwrites content every learner shares. Keeping them apart is what lets the controller
/// rate-limit and gate the two paths differently instead of charging a cached read against a
/// generation quota.
/// </summary>
public class LessonNoteWriter(
    AppDbContext db,
    IAiTextCompletion completion,
    IOptions<LessonOptions> options,
    ILogger<LessonNoteWriter> logger)
{
    private readonly LessonOptions _options = options.Value;

    public bool IsConfigured => completion.IsConfigured;

    /// <summary>Which provider and model is behind this, so generated text can say who wrote it.</summary>
    public string Provider => completion.Provider;

    public string Model => completion.Model;

    /// <summary>
    /// A raw completion through the same configured provider. Exposed so the lesson tutor can
    /// share this class's provider wiring rather than taking its own dependency on the client and
    /// drifting out of step with how notes are produced.
    /// </summary>
    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct) =>
        completion.CompleteJsonAsync(systemPrompt, userPrompt, ct);

    /// <summary>
    /// Generates the notes for a topic and stores them, replacing any existing copy.
    /// Throws <see cref="AiConfigurationException"/> or <see cref="AiProviderException"/> when the
    /// provider cannot deliver; the caller decides whether that degrades a page or fails a request.
    /// </summary>
    public async Task<LessonContent> WriteAsync(LessonTopic topic, Certification cert, CancellationToken ct)
    {
        if (!completion.IsConfigured)
            throw new AiConfigurationException(
                "No AI provider is configured. Set Ai:Provider and Ai:ApiKey to generate lesson notes.");

        // Cross-reference targets: the model is told to prefer naming topics the learner is
        // actually studying, which keeps the "confused with" bullets inside the syllabus.
        var siblings = await db.LessonTopics
            .Where(t => t.CertificationId == cert.Id && t.Id != topic.Id && t.DomainId == topic.DomainId)
            .OrderBy(t => t.Order)
            .Select(t => t.Title)
            .Take(_options.CrossReferenceCount)
            .ToListAsync(ct);

        var prompt = LessonPromptBuilder.Build(topic, cert, siblings);
        var raw = await completion.CompleteJsonAsync(LessonPromptBuilder.SystemPrompt, prompt, ct);
        var generated = LessonJsonParser.Parse(raw);

        if (!GeneratedLessonValidator.IsValid(generated, out var reason))
            throw new AiProviderException($"The generated notes were incomplete ({reason}). Try again.");

        var content = await db.LessonContents.FirstOrDefaultAsync(c => c.LessonTopicId == topic.Id, ct);
        var isNew = content is null;

        if (content is null)
        {
            content = new LessonContent { LessonTopicId = topic.Id };
            db.LessonContents.Add(content);
        }

        Apply(content, generated);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (isNew)
        {
            // Two learners opened the same never-generated lesson at once and both generated.
            // The unique index on LessonTopicId settles it; the loser reads the winner's row
            // rather than failing, because both were about to show the same thing anyway.
            logger.LogInformation(ex, "Lost a race writing notes for {Slug}; using the stored copy.", topic.Slug);
            db.Entry(content).State = EntityState.Detached;
            return await db.LessonContents.FirstAsync(c => c.LessonTopicId == topic.Id, ct);
        }

        logger.LogInformation(
            "Wrote lesson notes for {Slug} using {Provider}/{Model}.", topic.Slug, completion.Provider, completion.Model);

        return content;
    }

    private void Apply(LessonContent content, GeneratedLesson generated)
    {
        content.Overview = Trim(GeneratedLessonValidator.Clean(generated.Overview), 2000);
        content.UseCases = Trim(Join(generated.UseCases), 3000);
        content.CostNotes = Trim(GeneratedLessonValidator.Clean(generated.CostNotes), 2000);
        content.Integrations = Trim(Join(generated.Integrations), 3000);
        content.RealWorldExample = Trim(GeneratedLessonValidator.Clean(generated.RealWorldExample), 3000);
        content.ExamTraps = Trim(Join(generated.ExamTraps), 3000);
        content.Provider = completion.Provider;
        content.Model = Trim(completion.Model, 100);
        content.GeneratedAt = DateTime.UtcNow;
    }

    /// <summary>Bullets are stored newline-separated, which is how the DTO splits them back out.</summary>
    private static string Join(IEnumerable<string> values) =>
        string.Join('\n', GeneratedLessonValidator.Bullets(values));

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
