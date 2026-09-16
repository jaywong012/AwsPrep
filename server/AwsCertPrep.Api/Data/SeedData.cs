using AwsCertPrep.Api.Domain;
using AwsCertPrep.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AwsCertPrep.Api.Data;

/// <summary>
/// Seeds the exam blueprints (domains + weights taken from the official AWS exam guides),
/// a small starter question pool, and every item in the embedded reference banks, so the app
/// is usable — and calibrated against real exam-style items — before any AI generation runs.
/// Idempotent: safe to call on every startup.
/// </summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(
        AppDbContext db, ReferenceBank referenceBank, CancellationToken ct = default)
    {
        foreach (var blueprint in Blueprints)
        {
            var cert = await db.Certifications
                .Include(c => c.Domains)
                .FirstOrDefaultAsync(c => c.Code == blueprint.Code, ct);

            if (cert is null)
            {
                cert = new Certification
                {
                    Code = blueprint.Code,
                    Domains = blueprint.Domains
                        .Select(d => new CertificationDomain { Name = d.Name, WeightPercent = d.Weight })
                        .ToList()
                };
                Apply(cert, blueprint);
                db.Certifications.Add(cert);
            }
            else
            {
                Apply(cert, blueprint);

                foreach (var d in blueprint.Domains)
                {
                    var existing = cert.Domains.FirstOrDefault(x => x.Name == d.Name);
                    if (existing is null)
                        cert.Domains.Add(new CertificationDomain { Name = d.Name, WeightPercent = d.Weight });
                    else
                        existing.WeightPercent = d.Weight;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await SeedStarterQuestionsAsync(db, ct);
        await SeedReferenceQuestionsAsync(db, referenceBank, ct);
        await SeedLessonTopicsAsync(db, ct);
    }

    /// <summary>
    /// Imports the study curriculum from <see cref="LessonCatalog"/>. Updates in place rather
    /// than skipping: the catalogue is hand-maintained, so correcting a purpose or a docs URL
    /// here must reach an existing database on the next start. Generated lesson bodies hang off
    /// the topic and are left alone - re-seeding never discards them.
    /// </summary>
    private static async Task SeedLessonTopicsAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var (code, topics) in LessonCatalog.ByCertification)
        {
            var cert = await db.Certifications
                .Include(c => c.Domains)
                .FirstOrDefaultAsync(c => c.Code == code, ct);

            if (cert is null) continue;

            var existing = await db.LessonTopics
                .Where(t => t.CertificationId == cert.Id)
                .ToDictionaryAsync(t => t.Slug, StringComparer.OrdinalIgnoreCase, ct);

            var order = 0;

            foreach (var source in topics)
            {
                order++;

                var domain = cert.Domains.FirstOrDefault(d => d.Name == source.Domain);

                if (!existing.TryGetValue(source.Slug, out var topic))
                {
                    topic = new LessonTopic { CertificationId = cert.Id, Slug = source.Slug };
                    db.LessonTopics.Add(topic);
                }

                topic.DomainId = domain?.Id;
                topic.Title = source.Title;
                topic.Category = source.Category;
                topic.Purpose = source.Purpose;
                topic.PricingModel = source.PricingModel;
                topic.DocsUrl = source.DocsUrl;
                topic.PricingUrl = source.PricingUrl;
                topic.ServiceTags = source.ServiceTags;
                topic.Order = order;
                topic.IsCore = source.IsCore;
            }

            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Imports the embedded reference banks (real exam-style items, classified by domain and
    /// difficulty in <see cref="ReferenceBank"/>). Dedupes on the same stem hash the generator
    /// uses, so an item already present — seeded, imported or generated — is never duplicated.
    /// </summary>
    private static async Task SeedReferenceQuestionsAsync(
        AppDbContext db, ReferenceBank referenceBank, CancellationToken ct)
    {
        foreach (var code in referenceBank.CertificationCodes)
        {
            var cert = await db.Certifications
                .Include(c => c.Domains)
                .FirstOrDefaultAsync(c => c.Code == code, ct);

            if (cert is null) continue;

            var existingHashes = (await db.Questions
                .Where(q => q.CertificationId == cert.Id)
                .Select(q => q.StemHash)
                .ToListAsync(ct)).ToHashSet();

            var added = 0;

            foreach (var item in referenceBank.For(code))
            {
                var hash = QuestionHasher.Hash(item.Stem);
                if (!existingHashes.Add(hash)) continue;

                var domain = item.DomainName is null
                    ? null
                    : cert.Domains.FirstOrDefault(d => d.Name == item.DomainName);

                db.Questions.Add(new Question
                {
                    CertificationId = cert.Id,
                    DomainId = domain?.Id,
                    Stem = item.Stem,
                    Type = item.IsMultipleResponse ? QuestionType.MultipleChoice : QuestionType.SingleChoice,
                    Difficulty = item.Difficulty,
                    Source = QuestionSource.Reference,
                    Explanation = Truncate(item.Explanation, 4000),
                    ServiceTags = item.ServiceTags.Count == 0
                        ? null
                        : Truncate(string.Join(", ", item.ServiceTags), 400),
                    StemHash = hash,
                    Options = item.Options
                        .Select(o => new QuestionOption
                        {
                            Label = o.Label,
                            Text = Truncate(o.Text, 1000),
                            IsCorrect = o.IsCorrect
                        })
                        .ToList()
                });

                added++;
            }

            if (added > 0) await db.SaveChangesAsync(ct);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static void Apply(Certification cert, Blueprint b)
    {
        cert.Name = b.Name;
        cert.Description = b.Description;
        cert.Level = b.Level;
        cert.PassingScaledScore = b.PassingScaledScore;
        // AWS does not publish the raw-to-scaled mapping, so the percentage threshold is
        // the linear equivalent of the scaled pass mark on the 100-1000 scale.
        cert.PassingScore = (int)Math.Round((b.PassingScaledScore - 100) / 9.0);
        cert.ExamQuestionCount = b.ScoredQuestionCount + b.UnscoredQuestionCount;
        cert.ScoredQuestionCount = b.ScoredQuestionCount;
        cert.UnscoredQuestionCount = b.UnscoredQuestionCount;
        cert.DurationMinutes = b.DurationMinutes;
        cert.TargetCandidate = b.TargetCandidate;
        cert.OutOfScopeTasks = string.Join("\n", b.OutOfScopeTasks);
        cert.QuestionTypes = string.Join(", ", b.QuestionTypes);
        cert.ExamGuideUrl = b.ExamGuideUrl;
    }

    private static async Task SeedStarterQuestionsAsync(AppDbContext db, CancellationToken ct)
    {
        foreach (var group in StarterQuestions.GroupBy(q => q.CertCode))
        {
            var cert = await db.Certifications
                .Include(c => c.Domains)
                .FirstAsync(c => c.Code == group.Key, ct);

            foreach (var seed in group)
            {
                var hash = QuestionHasher.Hash(seed.Stem);
                if (await db.Questions.AnyAsync(q => q.CertificationId == cert.Id && q.StemHash == hash, ct))
                    continue;

                var domain = cert.Domains.FirstOrDefault(d => d.Name == seed.Domain);
                db.Questions.Add(new Question
                {
                    CertificationId = cert.Id,
                    DomainId = domain?.Id,
                    Stem = seed.Stem,
                    Type = seed.Correct.Length > 1 ? QuestionType.MultipleChoice : QuestionType.SingleChoice,
                    Difficulty = seed.Difficulty,
                    Source = QuestionSource.Seed,
                    Explanation = seed.Explanation,
                    ServiceTags = seed.Tags,
                    StemHash = hash,
                    Options = seed.Options
                        .Select((text, i) => new QuestionOption
                        {
                            Label = ((char)('A' + i)).ToString(),
                            Text = text,
                            IsCorrect = seed.Correct.Contains(((char)('A' + i)).ToString())
                        })
                        .ToList()
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private record DomainSpec(string Name, decimal Weight);

    /// <summary>
    /// Facts below come from the official AWS exam guides (see ExamGuideUrl on each entry):
    /// domain weights, question types, 50 scored + 15 unscored items, and the scaled pass mark.
    /// </summary>
    private record Blueprint(
        string Code,
        string Name,
        string Description,
        CertificationLevel Level,
        int PassingScaledScore,
        int ScoredQuestionCount,
        int UnscoredQuestionCount,
        int DurationMinutes,
        string TargetCandidate,
        string[] OutOfScopeTasks,
        string[] QuestionTypes,
        string ExamGuideUrl,
        DomainSpec[] Domains);

    private static readonly string[] TwoTypes = ["Multiple choice", "Multiple response"];
    private static readonly string[] FourTypes = ["Multiple choice", "Multiple response", "Ordering", "Matching"];

    private static readonly Blueprint[] Blueprints =
    [
        new("AIF-C01",
            "AWS Certified AI Practitioner",
            "Foundational validation of AI, ML and generative AI concepts on AWS, including Amazon Bedrock, SageMaker AI and responsible AI practices.",
            CertificationLevel.Foundational,
            700, 50, 15, 90,
            "Up to 6 months of exposure to AI/ML technologies on AWS. The candidate uses but does not necessarily build AI/ML solutions, and is familiar with core AWS services, the shared responsibility model, IAM and service pricing models.",
            [
                "Developing or coding AI/ML models or algorithms",
                "Implementing data engineering or feature engineering techniques",
                "Performing hyperparameter tuning or model optimization",
                "Building and deploying AI/ML pipelines or infrastructure",
                "Conducting mathematical or statistical analysis of AI/ML models",
                "Implementing security or compliance protocols for AI/ML systems",
                "Developing and implementing governance frameworks and policies"
            ],
            FourTypes,
            "https://docs.aws.amazon.com/aws-certification/latest/ai-practitioner-01/ai-practitioner-01.html",
            [
                new("Fundamentals of AI and ML", 20m),
                new("Fundamentals of Generative AI", 24m),
                new("Applications of Foundation Models", 28m),
                new("Guidelines for Responsible AI", 14m),
                new("Security, Compliance, and Governance for AI Solutions", 14m)
            ]),

        new("CLF-C02",
            "AWS Certified Cloud Practitioner",
            "Foundational understanding of AWS Cloud concepts, core services, security, architecture, pricing and support.",
            CertificationLevel.Foundational,
            700, 50, 15, 90,
            "Up to 6 months of exposure to AWS Cloud design, implementation and/or operations. Often early in an AWS career, or working alongside people in AWS Cloud roles.",
            [
                "Coding",
                "Designing cloud architecture",
                "Troubleshooting",
                "Implementation",
                "Load and performance testing"
            ],
            TwoTypes,
            "https://docs.aws.amazon.com/aws-certification/latest/cloud-practitioner-02/cloud-practitioner-02.html",
            [
                new("Cloud Concepts", 24m),
                new("Security and Compliance", 30m),
                new("Cloud Technology and Services", 34m),
                new("Billing, Pricing, and Support", 12m)
            ]),

        new("SAA-C03",
            "AWS Certified Solutions Architect - Associate",
            "Designing secure, resilient, high-performing and cost-optimized architectures on AWS.",
            CertificationLevel.Associate,
            720, 50, 15, 130,
            "At least 1 year of hands-on experience designing cloud solutions that use AWS services, working from the AWS Well-Architected Framework.",
            [],
            TwoTypes,
            "https://docs.aws.amazon.com/aws-certification/latest/solutions-architect-associate-03/solutions-architect-associate-03.html",
            [
                new("Design Secure Architectures", 30m),
                new("Design Resilient Architectures", 26m),
                new("Design High-Performing Architectures", 24m),
                new("Design Cost-Optimized Architectures", 20m)
            ]),

        new("MLA-C01",
            "AWS Certified Machine Learning Engineer - Associate",
            "Building, deploying, orchestrating and monitoring ML solutions on AWS with SageMaker AI and related services.",
            CertificationLevel.Associate,
            720, 50, 15, 130,
            "At least 1 year of experience with Amazon SageMaker and other AWS services for ML engineering, plus 1 year in a related role such as backend developer, DevOps developer, data engineer or data scientist.",
            [
                "Designing and architecting full end-to-end ML solutions",
                "Setting up best practices and guiding ML strategies",
                "Handling integration with a wide array of services or new tools and technologies",
                "Working deeply in two or more ML domains (for example, NLP or computer vision)",
                "Quantizing models and analyzing the impact on accuracy"
            ],
            FourTypes,
            "https://docs.aws.amazon.com/aws-certification/latest/machine-learning-engineer-associate-01/machine-learning-engineer-associate-01.html",
            [
                new("Data Preparation for Machine Learning", 28m),
                new("ML Model Development", 26m),
                new("Deployment and Orchestration of ML Workflows", 22m),
                new("ML Solution Monitoring, Maintenance, and Security", 24m)
            ])
    ];

    private record StarterQuestion(
        string CertCode,
        string Domain,
        string Stem,
        string[] Options,
        string[] Correct,
        string Explanation,
        Difficulty Difficulty,
        string Tags);

    private static readonly StarterQuestion[] StarterQuestions =
    [
        new("AIF-C01", "Fundamentals of Generative AI",
            "A team wants to improve a foundation model's answers by supplying relevant internal documents at inference time, without changing the model weights. Which approach should they use?",
            [
                "Retrieval Augmented Generation (RAG) with a vector store",
                "Continued pre-training on the document corpus",
                "Full fine-tuning of the foundation model",
                "Increasing the model's temperature setting"
            ],
            ["A"],
            "RAG retrieves relevant context at inference time and injects it into the prompt, leaving model weights untouched. Pre-training and fine-tuning both modify weights; temperature only affects output randomness.",
            Difficulty.Easy, "Amazon Bedrock,Knowledge Bases"),

        new("AIF-C01", "Applications of Foundation Models",
            "Which Amazon Bedrock capability lets you enforce content filters and denied topics on both prompts and model responses?",
            [
                "Guardrails for Amazon Bedrock",
                "Amazon Bedrock Agents",
                "Provisioned Throughput",
                "Model evaluation jobs"
            ],
            ["A"],
            "Guardrails apply configurable content filters, denied topics, word filters and PII redaction to prompts and responses across models.",
            Difficulty.Easy, "Amazon Bedrock,Guardrails"),

        new("AIF-C01", "Fundamentals of AI and ML",
            "Select the two situations that indicate a supervised learning problem.",
            [
                "Predicting next month's sales from labelled historical sales data",
                "Classifying support tickets using a dataset of tickets with known categories",
                "Grouping customers into segments with no predefined labels",
                "Reducing the dimensionality of a feature set for visualisation",
                "Training an agent through trial-and-error rewards"
            ],
            ["A", "B"],
            "Supervised learning requires labelled targets. Clustering and dimensionality reduction are unsupervised; trial-and-error rewards describe reinforcement learning.",
            Difficulty.Medium, "Amazon SageMaker AI"),

        new("AIF-C01", "Guidelines for Responsible AI",
            "Which AWS feature produces a report describing a model's intended uses, limitations and risk ratings for governance reviews?",
            [
                "Amazon SageMaker Model Cards",
                "Amazon SageMaker Feature Store",
                "AWS Artifact",
                "Amazon CloudWatch Logs Insights"
            ],
            ["A"],
            "Model Cards document intended use, limitations, evaluation results and risk ratings for a model, supporting responsible-AI governance.",
            Difficulty.Medium, "Amazon SageMaker AI,Model Cards"),

        new("AIF-C01", "Security, Compliance, and Governance for AI Solutions",
            "A company must guarantee that prompts sent to Amazon Bedrock never traverse the public internet. What should they implement?",
            [
                "An interface VPC endpoint (AWS PrivateLink) for Amazon Bedrock",
                "A NAT gateway in a public subnet",
                "An internet gateway with a restrictive route table",
                "Server-side encryption with AWS KMS"
            ],
            ["A"],
            "Interface VPC endpoints powered by AWS PrivateLink keep traffic between the VPC and Bedrock on the AWS network. KMS protects data at rest, not the network path.",
            Difficulty.Medium, "Amazon Bedrock,AWS PrivateLink"),

        new("CLF-C02", "Cloud Concepts",
            "Which statement best describes the AWS shared responsibility model?",
            [
                "AWS is responsible for security of the cloud; the customer is responsible for security in the cloud",
                "AWS is responsible for all security controls in every service",
                "The customer is responsible for patching the hypervisor of EC2 hosts",
                "Responsibility depends solely on the AWS Support plan purchased"
            ],
            ["A"],
            "AWS secures the underlying infrastructure (security 'of' the cloud); customers secure their data, identity configuration, OS and network settings (security 'in' the cloud).",
            Difficulty.Easy, "Shared Responsibility Model"),

        new("CLF-C02", "Billing, Pricing, and Support",
            "A workload runs steadily 24/7 for the next three years on EC2. Which purchasing option gives the largest discount over On-Demand?",
            [
                "Compute Savings Plans or Reserved Instances with a 3-year term",
                "Spot Instances",
                "Dedicated Hosts billed On-Demand",
                "On-Demand with an AWS Enterprise Support plan"
            ],
            ["A"],
            "Steady, predictable long-running usage is the ideal fit for a 3-year commitment (Savings Plans / RIs). Spot suits interruptible workloads and offers no capacity guarantee.",
            Difficulty.Easy, "Amazon EC2,Savings Plans"),

        new("CLF-C02", "Cloud Technology and Services",
            "Which AWS service should be used to store static website assets with the highest durability and lowest operational overhead?",
            [
                "Amazon S3",
                "Amazon EBS",
                "Amazon EFS",
                "AWS Storage Gateway"
            ],
            ["A"],
            "Amazon S3 is object storage designed for 11 nines of durability and can serve static website content directly, with no servers to manage.",
            Difficulty.Easy, "Amazon S3"),

        new("CLF-C02", "Security and Compliance",
            "Select the two AWS services that help detect suspicious activity and evaluate resource compliance respectively.",
            [
                "Amazon GuardDuty",
                "AWS Config",
                "Amazon Route 53",
                "AWS Batch",
                "Amazon Kinesis Data Firehose"
            ],
            ["A", "B"],
            "GuardDuty continuously analyses logs for threats; AWS Config records resource configuration and evaluates it against compliance rules.",
            Difficulty.Medium, "Amazon GuardDuty,AWS Config")
    ];
}
