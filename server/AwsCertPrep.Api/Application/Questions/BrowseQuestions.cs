using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Application.Questions;

/// <summary>
/// A page of the question bank.
///
/// The specification carries the filters so the repository builds one query with them applied in
/// the database, rather than the handler pulling rows back and filtering in memory.
/// </summary>
public class QuestionsForBankSpec : Specification<Question>
{
    public QuestionsForBankSpec(
        string certificationCode,
        int? domainId,
        Difficulty? difficulty,
        bool includeRetired,
        int skip,
        int take)
    {
        Where(q => q.Certification!.Code == certificationCode
                   && (includeRetired || q.RetiredReason == null)
                   && (domainId == null || q.DomainId == domainId)
                   && (difficulty == null || q.Difficulty == difficulty));

        AddInclude(q => q.Domain!);
        AddInclude(q => q.Options);

        SortByDescending(q => q.CreatedAt);
        Paginate(skip, take);
        NoTracking();
    }
}

public record BrowseQuestionsQuery(
    [property: Required] string CertificationCode,
    int? DomainId,
    Difficulty? Difficulty,
    bool IncludeRetired,
    [property: Range(0, int.MaxValue)] int Skip,
    [property: Range(1, 100)] int Take) : IQuery<IReadOnlyList<QuestionDto>>;

public class BrowseQuestionsHandler(IUnitOfWork uow)
    : IQueryHandler<BrowseQuestionsQuery, IReadOnlyList<QuestionDto>>
{
    public async Task<IReadOnlyList<QuestionDto>> HandleAsync(
        BrowseQuestionsQuery request, CancellationToken ct)
    {
        var spec = new QuestionsForBankSpec(
            request.CertificationCode,
            request.DomainId,
            request.Difficulty,
            request.IncludeRetired,
            request.Skip,
            request.Take);

        var found = await uow.Repository<Question>().ListAsync(spec, ct);

        return found.Select(q => QuestionMapper.ToDto(q, includeAnswers: true)).ToList();
    }
}

/// <summary>
/// Reports stored questions that break the official item format or fall outside the
/// certification's scope. A dry run: nothing is modified.
/// </summary>
public record AuditQuestionsQuery(string? CertificationCode) : IQuery<AuditReportDto>;

public class AuditQuestionsHandler(Services.QuestionAuditService audit)
    : IQueryHandler<AuditQuestionsQuery, AuditReportDto>
{
    public Task<AuditReportDto> HandleAsync(AuditQuestionsQuery request, CancellationToken ct) =>
        audit.AuditAsync(request.CertificationCode, ct);
}

/// <summary>
/// Applies the audit: deletes flagged questions nobody has answered and retires the ones
/// referenced by exam history, so past results stay explainable.
/// </summary>
public record CleanAuditedQuestionsCommand(string? CertificationCode) : ICommand<AuditCleanupDto>;

public class CleanAuditedQuestionsHandler(Services.QuestionAuditService audit)
    : ICommandHandler<CleanAuditedQuestionsCommand, AuditCleanupDto>
{
    public Task<AuditCleanupDto> HandleAsync(CleanAuditedQuestionsCommand request, CancellationToken ct) =>
        audit.CleanAsync(request.CertificationCode, ct);
}
