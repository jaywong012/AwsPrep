namespace AwsCertPrep.Api.Domain;

/// <summary>
/// One study topic in a certification's curriculum - usually an AWS service, sometimes a
/// concept such as the shared responsibility model.
///
/// Everything on this entity is seeded from the exam guide and the official AWS documentation
/// (see <see cref="Data.LessonCatalog"/>), never generated. That split is deliberate: a learner
/// must be able to trust the service name, what it does and where the official page is, so those
/// facts never come from a language model. The depth - use cases, cost in practice, integrations,
/// a worked real-world example - is generated once per topic and cached as <see cref="LessonContent"/>.
/// </summary>
public class LessonTopic
{
    public int Id { get; set; }

    public int CertificationId { get; set; }
    public Certification? Certification { get; set; }

    /// <summary>The exam domain this topic is studied under. Drives weak-domain ordering.</summary>
    public int? DomainId { get; set; }
    public CertificationDomain? Domain { get; set; }

    /// <summary>URL-safe identifier, e.g. "amazon-s3". Stable: it is what the client routes on.</summary>
    public string Slug { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Grouping shown in the list, e.g. "Storage", "Security, Identity, and Compliance".</summary>
    public string Category { get; set; } = "";

    /// <summary>One sentence on what the service is for. Seeded, so it is always accurate.</summary>
    public string Purpose { get; set; } = "";

    /// <summary>How AWS charges for it, in one line. Seeded.</summary>
    public string PricingModel { get; set; } = "";

    /// <summary>Official AWS page for this topic. Every lesson links out to the real docs.</summary>
    public string DocsUrl { get; set; } = "";

    /// <summary>Official pricing page, where the service has one.</summary>
    public string? PricingUrl { get; set; }

    /// <summary>
    /// Comma-separated: every name a question might be tagged with for this topic, including the
    /// abbreviation and the sub-features. This is the join between the curriculum and the learner's
    /// own answer history, so the lesson list can be ordered by what they actually get wrong.
    /// Null for concept topics that no service name identifies.
    /// </summary>
    public string? ServiceTags { get; set; }

    /// <summary>Curriculum order within the certification. Lower comes first.</summary>
    public int Order { get; set; }

    /// <summary>
    /// A topic the exam leans on heavily. Used to break ties when a learner has no history yet,
    /// so a cold start still opens with the services the exam actually tests most.
    /// </summary>
    public bool IsCore { get; set; }

    public LessonContent? Content { get; set; }
}

/// <summary>
/// The generated depth for one topic, cached after the first request so a lesson costs one
/// provider call for its lifetime. Regenerating replaces it in place.
/// </summary>
public class LessonContent
{
    public int Id { get; set; }

    public int LessonTopicId { get; set; }
    public LessonTopic? LessonTopic { get; set; }

    /// <summary>Two or three sentences expanding the seeded one-line purpose.</summary>
    public string Overview { get; set; } = "";

    /// <summary>Newline-separated bullets: the situations the exam describes for this service.</summary>
    public string UseCases { get; set; } = "";

    /// <summary>What actually drives the bill, and what is free. Expands the seeded pricing model.</summary>
    public string CostNotes { get; set; } = "";

    /// <summary>Newline-separated "Other service - why they are used together" bullets.</summary>
    public string Integrations { get; set; } = "";

    /// <summary>A concrete worked scenario a company would really face.</summary>
    public string RealWorldExample { get; set; } = "";

    /// <summary>
    /// Newline-separated bullets naming the services this one is confused with on the exam and
    /// the line that separates them. This is the section that most directly moves a score.
    /// </summary>
    public string ExamTraps { get; set; } = "";

    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Per-learner progress through a topic, keyed by the same anonymous browser key the exam
/// sessions use. Viewing is tracked separately from completing: opening a lesson should not
/// silently mark it done.
/// </summary>
public class LessonProgress
{
    public int Id { get; set; }

    public int LessonTopicId { get; set; }
    public LessonTopic? LessonTopic { get; set; }

    public string UserKey { get; set; } = "local";

    public DateTime? CompletedAt { get; set; }
    public DateTime LastViewedAt { get; set; } = DateTime.UtcNow;
    public int ViewCount { get; set; }
}
