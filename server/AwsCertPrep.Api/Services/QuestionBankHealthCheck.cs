using AwsCertPrep.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Readiness check for the thing this API exists to do: serve questions. A reachable database
/// with an empty bank answers every exam request with an error, so it is reported as degraded
/// rather than healthy - the deployment is up, but it is not ready to be useful.
/// </summary>
public class QuestionBankHealthCheck(AppDbContext db, ReferenceBank referenceBank) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var certifications = await db.Certifications.CountAsync(cancellationToken);
        var questions = await db.Questions.CountAsync(q => q.RetiredReason == null, cancellationToken);
        var referenceItems = referenceBank.CertificationCodes.Sum(code => referenceBank.For(code).Count);

        var description =
            $"{certifications} certification(s), {questions} live question(s), {referenceItems} reference item(s) embedded.";

        if (certifications == 0)
            return HealthCheckResult.Unhealthy($"Exam blueprints are not seeded. {description}");

        return questions == 0
            ? HealthCheckResult.Degraded($"The question bank is empty. {description}")
            : HealthCheckResult.Healthy(description);
    }
}
