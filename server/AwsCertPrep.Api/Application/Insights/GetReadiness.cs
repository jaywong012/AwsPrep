using System.ComponentModel.DataAnnotations;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Dtos;
using AwsCertPrep.Api.Ml;

namespace AwsCertPrep.Api.Application.Insights;

/// <summary>
/// ML.NET-backed readiness projection and per-domain study recommendations.
///
/// A query despite being expensive: it changes nothing, and a cold call training a model is a
/// cost concern for the rate limiter, not a reason to call it a command.
/// </summary>
public record ReadinessQuery(
    [property: Required] string CertificationCode,
    [property: Required] string UserKey) : IQuery<ReadinessDto>;

public class ReadinessHandler(ReadinessService readiness) : IQueryHandler<ReadinessQuery, ReadinessDto>
{
    public Task<ReadinessDto> HandleAsync(ReadinessQuery request, CancellationToken ct) =>
        readiness.AnalyzeAsync(request.CertificationCode, request.UserKey, ct);
}
