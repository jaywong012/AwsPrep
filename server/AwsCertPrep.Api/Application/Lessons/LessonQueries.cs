using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Application.Lessons;

/// <summary>
/// The curriculum for a certification, ordered by what this learner should study next.
/// </summary>
public record ListLessonsQuery(
    [property: Required] string CertificationCode,
    [property: Required] string UserKey) : IQuery<LessonsResponse>;

public class ListLessonsHandler(IUnitOfWork uow, LessonReadModel read)
    : IQueryHandler<ListLessonsQuery, LessonsResponse>
{
    public async Task<LessonsResponse> HandleAsync(ListLessonsQuery request, CancellationToken ct)
    {
        var cert = await read.FindCertificationAsync(request.CertificationCode, ct);

        var topics = await uow.Repository<LessonTopic>()
            .Query()
            .Include(t => t.Domain)
            .Where(t => t.CertificationId == cert.Id)
            .OrderBy(t => t.Order)
            .ToListAsync(ct);

        if (topics.Count == 0)
            return new LessonsResponse(cert.Code, cert.Name, 0, 0, read.AiConfigured, []);

        // Four queries regardless of how many topics there are: each per-topic fact the list
        // needs is loaded in one go and joined in memory.
        var mastery = await read.MasteryIndexAsync(cert.Id, request.UserKey, ct);
        var completedIds = await read.CompletedTopicIdsAsync(cert.Id, request.UserKey, ct);

        var withNotes = (await uow.Repository<LessonContent>()
                .Query()
                .Where(c => c.LessonTopic!.CertificationId == cert.Id)
                .Select(c => c.LessonTopicId)
                .ToListAsync(ct))
            .ToHashSet();

        // Ranked in memory because mastery is computed in memory. Sorting the topics themselves
        // rather than the projected DTOs keeps curriculum order available as a tie-break without
        // looking each topic up again by slug.
        var ranked = topics
            .Select(topic => (Topic: topic, Mastery: mastery.For(topic)))
            .OrderBy(x => LessonReadModel.Priority(x.Mastery.Status, x.Topic.IsCore))
            .ThenBy(x => x.Mastery.AccuracyPercent ?? 100m)
            .ThenBy(x => x.Topic.Order)
            .Select(x => new LessonSummaryDto(
                x.Topic.Slug,
                x.Topic.Title,
                x.Topic.Category,
                Data.LessonCatalog.KindOf(x.Topic.Category).ToString(),
                x.Topic.Domain?.Name,
                x.Topic.Purpose,
                x.Topic.IsCore,
                withNotes.Contains(x.Topic.Id),
                completedIds.Contains(x.Topic.Id),
                x.Mastery))
            .ToList();

        return new LessonsResponse(
            cert.Code, cert.Name, ranked.Count, completedIds.Count, read.AiConfigured, ranked);
    }
}

/// <summary>
/// One lesson, read only.
///
/// Never calls the provider: a topic with no notes yet comes back with a null body, and the
/// client asks for them with <see cref="WriteLessonNotesCommand"/>. The split is what lets a
/// cached lesson be read freely while generation stays limited, and it means the page renders its
/// verified facts immediately instead of waiting on a model.
/// </summary>
public record GetLessonQuery(
    [property: Required] string CertificationCode,
    [property: Required] string Slug,
    [property: Required] string UserKey) : IQuery<LessonDetailDto>;

public class GetLessonHandler(IUnitOfWork uow, LessonReadModel read, ILogger<GetLessonHandler> logger)
    : IQueryHandler<GetLessonQuery, LessonDetailDto>
{
    public async Task<LessonDetailDto> HandleAsync(GetLessonQuery request, CancellationToken ct)
    {
        var (cert, topic) = await read.FindTopicAsync(request.CertificationCode, request.Slug, ct);

        await RecordViewAsync(topic.Id, request.UserKey, ct);

        return await read.BuildDetailAsync(cert, topic, request.UserKey, warning: null, ct);
    }

    /// <summary>
    /// Counts a lesson as opened. Deliberately a write on a read path, and deliberately forgiving:
    /// this is study telemetry, so losing a racing insert must not fail the page that was asked for.
    /// </summary>
    private async Task RecordViewAsync(int topicId, string userKey, CancellationToken ct)
    {
        var progress = uow.Repository<LessonProgress>();

        var row = await progress
            .Query(tracked: true)
            .FirstOrDefaultAsync(p => p.UserKey == userKey && p.LessonTopicId == topicId, ct);

        if (row is null)
        {
            row = new LessonProgress { LessonTopicId = topicId, UserKey = userKey };
            progress.Add(row);
        }

        row.LastViewedAt = DateTime.UtcNow;
        row.ViewCount++;

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogDebug(ex, "Ignored a racing lesson view for topic {TopicId}.", topicId);
            uow.Detach(row);
        }
    }
}
