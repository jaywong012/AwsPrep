using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Application.Certifications;

/// <summary>
/// The certifications on offer with their domains and question counts.
/// <paramref name="Code"/> narrows it to one; null returns all.
/// </summary>
public record ListCertificationsQuery(string? Code = null) : IQuery<IReadOnlyList<CertificationDto>>;

public class ListCertificationsHandler(IUnitOfWork uow)
    : IQueryHandler<ListCertificationsQuery, IReadOnlyList<CertificationDto>>
{
    public async Task<IReadOnlyList<CertificationDto>> HandleAsync(
        ListCertificationsQuery request, CancellationToken ct)
    {
        var certs = await uow.Repository<Certification>()
            .Query()
            .Include(c => c.Domains)
            .Where(c => request.Code == null || c.Code == request.Code)
            .OrderBy(c => c.Code)
            .ToListAsync(ct);

        if (certs.Count == 0) return [];

        var certIds = certs.Select(c => c.Id).ToList();
        var questions = uow.Repository<Question>().Query();

        // Grouped in the database rather than counted per certification, so this stays two
        // queries whatever the number of certifications or domains.
        var counts = await questions
            .Where(q => certIds.Contains(q.CertificationId) && q.RetiredReason == null)
            .GroupBy(q => new { q.CertificationId, q.DomainId })
            .Select(g => new { g.Key.CertificationId, g.Key.DomainId, Count = g.Count() })
            .ToListAsync(ct);

        // Retired items are excluded from the counts learners see, and reported separately.
        var retired = await questions
            .Where(q => certIds.Contains(q.CertificationId) && q.RetiredReason != null)
            .GroupBy(q => q.CertificationId)
            .Select(g => new { CertificationId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return certs.Select(c => new CertificationDto(
                c.Id,
                c.Code,
                c.Name,
                c.Description,
                c.Level,
                c.PassingScore,
                c.PassingScaledScore,
                c.ExamQuestionCount,
                c.ScoredQuestionCount,
                c.UnscoredQuestionCount,
                c.DurationMinutes,
                c.QuestionTypes,
                c.TargetCandidate,
                c.ExamGuideUrl,
                counts.Where(x => x.CertificationId == c.Id).Sum(x => x.Count),
                retired.FirstOrDefault(x => x.CertificationId == c.Id)?.Count ?? 0,
                c.Domains
                    .OrderByDescending(d => d.WeightPercent)
                    .Select(d => new DomainDto(
                        d.Id,
                        d.Name,
                        d.WeightPercent,
                        counts.FirstOrDefault(x => x.CertificationId == c.Id && x.DomainId == d.Id)?.Count ?? 0))
                    .ToList()))
            .ToList();
    }
}
