using AwsCertPrep.Api.Data;
using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Audits stored questions against the official item format and the certification's scope,
/// using the same rules the generator is held to.
///
/// Cleanup is deliberately two-tier: an item nobody has answered is deleted outright, while an
/// item referenced by exam history is retired instead, so past scores stay explainable.
/// </summary>
public class QuestionAuditService(AppDbContext db, ILogger<QuestionAuditService> logger)
{
    public async Task<AuditReportDto> AuditAsync(string? certificationCode, CancellationToken ct)
    {
        var findings = await FindAsync(certificationCode, ct);

        return new AuditReportDto(
            findings.Count,
            await CountAsync(certificationCode, ct),
            findings
                .GroupBy(f => f.Reason)
                .OrderByDescending(g => g.Count())
                .Select(g => new AuditReasonDto(g.Key, g.Count()))
                .ToList(),
            findings
                .Select(f => new AuditFindingDto(
                    f.Question.Id,
                    f.Question.Certification!.Code,
                    f.Question.Difficulty,
                    f.Question.Type,
                    Truncate(f.Question.Stem, 160),
                    f.Reason,
                    f.Question.RetiredReason != null))
                .ToList());
    }

    public async Task<AuditCleanupDto> CleanAsync(string? certificationCode, CancellationToken ct)
    {
        var findings = await FindAsync(certificationCode, ct);

        var referencedIds = await db.ExamSessionQuestions
            .Where(i => findings.Select(f => f.Question.Id).Contains(i.QuestionId))
            .Select(i => i.QuestionId)
            .Distinct()
            .ToListAsync(ct);

        var deleted = 0;
        var retired = 0;

        foreach (var (question, reason) in findings)
        {
            if (referencedIds.Contains(question.Id))
            {
                if (question.RetiredReason is null)
                {
                    question.RetiredReason = Truncate(reason, 300);
                    question.RetiredAt = DateTime.UtcNow;
                    retired++;
                }
            }
            else
            {
                db.Questions.Remove(question);
                deleted++;
            }
        }

        if (deleted > 0 || retired > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Question audit cleanup: deleted {Deleted}, retired {Retired} of {Total} flagged.",
            deleted, retired, findings.Count);

        return new AuditCleanupDto(
            findings.Count,
            deleted,
            retired,
            findings
                .GroupBy(f => f.Reason)
                .OrderByDescending(g => g.Count())
                .Select(g => new AuditReasonDto(g.Key, g.Count()))
                .ToList());
    }

    private async Task<List<(Question Question, string Reason)>> FindAsync(
        string? certificationCode, CancellationToken ct)
    {
        var query = db.Questions
            .Include(q => q.Certification)
            .Include(q => q.Options)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(certificationCode))
            query = query.Where(q => q.Certification!.Code == certificationCode);

        var questions = await query.ToListAsync(ct);
        var findings = new List<(Question, string)>();

        foreach (var q in questions)
        {
            // Reuse the generator's validator by projecting the stored row back onto its shape,
            // so stored items and freshly generated ones are judged identically.
            var candidate = new GeneratedQuestion
            {
                Stem = q.Stem,
                Explanation = q.Explanation,
                Options = q.Options
                    .Select(o => new GeneratedOption { Label = o.Label, Text = o.Text, IsCorrect = o.IsCorrect })
                    .ToList(),
            };

            if (!GeneratedQuestionValidator.IsValid(candidate, q.Certification!.Level, out var reason))
            {
                findings.Add((q, reason));
                continue;
            }

            // Stored-only checks the generator cannot hit.
            var declaredCorrect = q.Options.Count(o => o.IsCorrect);
            var expectedType = declaredCorrect > 1 ? QuestionType.MultipleChoice : QuestionType.SingleChoice;
            if (declaredCorrect > 1 && q.Type != QuestionType.MultipleChoice)
                findings.Add((q, "stored type does not match its correct-answer count"));
            else if (declaredCorrect == 1 && q.Type != QuestionType.SingleChoice)
                findings.Add((q, "stored type does not match its correct-answer count"));
            else if (expectedType == QuestionType.MultipleChoice
                     && !q.Stem.Contains("Select TWO", StringComparison.OrdinalIgnoreCase)
                     && !q.Stem.Contains("two", StringComparison.OrdinalIgnoreCase))
                findings.Add((q, "multiple-response stem does not tell the candidate how many to select"));
        }

        return findings;
    }

    private Task<int> CountAsync(string? certificationCode, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(certificationCode)
            ? db.Questions.CountAsync(ct)
            : db.Questions.CountAsync(q => q.Certification!.Code == certificationCode, ct);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
