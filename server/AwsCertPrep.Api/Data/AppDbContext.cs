using AwsCertPrep.Api.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AwsCertPrep.Api.Data;

/// <summary>
/// One context for the study data and the Identity tables alike: one connection, one migration
/// history, and the existing readiness health check covers the accounts as well.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<CertificationDomain> CertificationDomains => Set<CertificationDomain>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamSessionQuestion> ExamSessionQuestions => Set<ExamSessionQuestion>();
    public DbSet<LessonTopic> LessonTopics => Set<LessonTopic>();
    public DbSet<LessonContent> LessonContents => Set<LessonContent>();
    public DbSet<LessonProgress> LessonProgress => Set<LessonProgress>();

    /// <summary>
    /// Every DateTime in this model is UTC, and SQL Server's datetime2 does not store that fact:
    /// a value read back comes out as <see cref="DateTimeKind.Unspecified"/>. System.Text.Json
    /// then writes it with no "Z", and a browser parses a timestamp with no offset as LOCAL time.
    ///
    /// That is not cosmetic. It broke the timed mock exam for anyone east or west of UTC: the
    /// countdown read a start time hours in the past, computed a deadline that had already
    /// passed, and auto-submitted the exam the moment it opened. Pinning the kind on the way out
    /// of the database is what makes a timestamp mean the same thing on both sides of the wire.
    /// </summary>
    public class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        value => value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTime>().HaveConversion(typeof(UtcDateTimeConverter));
        builder.Properties<DateTime?>().HaveConversion(typeof(NullableUtcDateTimeConverter));
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Identity maps its own tables here. Without this call the AspNet* tables are never
        // configured, the migration comes out empty, and every sign-in fails against a database
        // that looks migrated.
        base.OnModelCreating(b);

        b.Entity<Certification>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.TargetCandidate).HasMaxLength(1000);
            e.Property(x => x.OutOfScopeTasks).HasMaxLength(2000);
            e.Property(x => x.QuestionTypes).HasMaxLength(200);
            e.Property(x => x.ExamGuideUrl).HasMaxLength(400);
        });

        b.Entity<CertificationDomain>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.WeightPercent).HasPrecision(5, 2);
            e.HasOne(x => x.Certification)
             .WithMany(x => x.Domains)
             .HasForeignKey(x => x.CertificationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Question>(e =>
        {
            e.Property(x => x.Stem).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Explanation).HasMaxLength(4000);
            e.Property(x => x.ServiceTags).HasMaxLength(400);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.StemHash).HasMaxLength(64);
            e.Property(x => x.RetiredReason).HasMaxLength(300);
            e.HasIndex(x => new { x.CertificationId, x.StemHash }).IsUnique();
            e.HasIndex(x => new { x.CertificationId, x.RetiredReason });

            e.HasOne(x => x.Certification)
             .WithMany(x => x.Questions)
             .HasForeignKey(x => x.CertificationId)
             .OnDelete(DeleteBehavior.Cascade);

            // NoAction (not SetNull/Cascade): SQL Server rejects the second cascade path
            // Certifications -> CertificationDomains -> Questions alongside
            // Certifications -> Questions. Deleting a domain is an admin-only operation and
            // must reassign its questions first.
            e.HasOne(x => x.Domain)
             .WithMany()
             .HasForeignKey(x => x.DomainId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<QuestionOption>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(2).IsRequired();
            e.Property(x => x.Text).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.Question)
             .WithMany(x => x.Options)
             .HasForeignKey(x => x.QuestionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExamSession>(e =>
        {
            e.Property(x => x.UserKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ScorePercent).HasPrecision(5, 2);
            e.HasIndex(x => new { x.UserKey, x.CertificationId });
            e.HasOne(x => x.Certification)
             .WithMany()
             .HasForeignKey(x => x.CertificationId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LessonTopic>(e =>
        {
            e.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.Purpose).HasMaxLength(600).IsRequired();
            e.Property(x => x.PricingModel).HasMaxLength(600).IsRequired();
            e.Property(x => x.DocsUrl).HasMaxLength(400).IsRequired();
            e.Property(x => x.PricingUrl).HasMaxLength(400);
            e.Property(x => x.ServiceTags).HasMaxLength(600);

            // The slug is what the client routes on, so it has to be unique per certification.
            e.HasIndex(x => new { x.CertificationId, x.Slug }).IsUnique();

            e.HasOne(x => x.Certification)
             .WithMany()
             .HasForeignKey(x => x.CertificationId)
             .OnDelete(DeleteBehavior.Cascade);

            // NoAction for the same reason Question.Domain uses it: SQL Server refuses the
            // second cascade path Certifications -> CertificationDomains -> LessonTopics.
            e.HasOne(x => x.Domain)
             .WithMany()
             .HasForeignKey(x => x.DomainId)
             .OnDelete(DeleteBehavior.NoAction);
        });

        b.Entity<LessonContent>(e =>
        {
            e.Property(x => x.Overview).HasMaxLength(2000).IsRequired();
            e.Property(x => x.UseCases).HasMaxLength(3000).IsRequired();
            e.Property(x => x.CostNotes).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Integrations).HasMaxLength(3000).IsRequired();
            e.Property(x => x.RealWorldExample).HasMaxLength(3000).IsRequired();
            e.Property(x => x.ExamTraps).HasMaxLength(3000).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(50);
            e.Property(x => x.Model).HasMaxLength(100);

            // One cached body per topic; regenerating updates the row rather than adding one.
            e.HasIndex(x => x.LessonTopicId).IsUnique();

            e.HasOne(x => x.LessonTopic)
             .WithOne(x => x.Content)
             .HasForeignKey<LessonContent>(x => x.LessonTopicId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LessonProgress>(e =>
        {
            e.Property(x => x.UserKey).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.UserKey, x.LessonTopicId }).IsUnique();

            e.HasOne(x => x.LessonTopic)
             .WithMany()
             .HasForeignKey(x => x.LessonTopicId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ExamSessionQuestion>(e =>
        {
            e.Property(x => x.SelectedLabels).HasMaxLength(20);
            e.HasIndex(x => new { x.ExamSessionId, x.Order });
            e.HasOne(x => x.ExamSession)
             .WithMany(x => x.Items)
             .HasForeignKey(x => x.ExamSessionId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Question)
             .WithMany()
             .HasForeignKey(x => x.QuestionId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
