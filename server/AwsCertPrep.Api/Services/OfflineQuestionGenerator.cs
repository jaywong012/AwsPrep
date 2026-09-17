using AwsCertPrep.Api.Domain;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// No-API-key fallback. It composes questions from a local template bank so the app is
/// fully usable offline, but the output is template-based, NOT model-generated - the
/// response carries a warning saying so.
/// </summary>
public class OfflineQuestionGenerator : IQuestionGenerator
{
    public string Provider => "Offline";

    public Task<GenerationResult> GenerateAsync(GenerationContext ctx, CancellationToken ct)
    {
        // Deterministic-but-varied ordering seeded by the request so repeat calls surface new items.
        var seed = HashCode.Combine(ctx.CertificationCode, ctx.TargetDomain, ctx.Difficulty, ctx.ExistingStems.Count);
        var rng = new Random(seed);

        var pool = Templates
            .Where(t => t.CertCode == ctx.CertificationCode || t.CertCode == "*")
            .Where(t => ctx.TargetDomain is null || t.Domain == ctx.TargetDomain || t.CertCode == "*")
            .OrderBy(_ => rng.Next())
            .ToList();

        var existing = ctx.ExistingStems
            .Select(QuestionHasher.Hash)
            .ToHashSet();

        var chosen = new List<GeneratedQuestion>();
        foreach (var t in pool)
        {
            if (chosen.Count >= ctx.Count) break;
            if (existing.Contains(QuestionHasher.Hash(t.Stem))) continue;

            var options = t.Options
                .Select((text, i) => (text, correct: t.Correct.Contains(((char)('A' + i)).ToString())))
                .OrderBy(_ => rng.Next())
                .Select((o, i) => new GeneratedOption
                {
                    Label = ((char)('A' + i)).ToString(),
                    Text = o.text,
                    IsCorrect = o.correct
                })
                .ToList();

            chosen.Add(new GeneratedQuestion
            {
                Stem = t.Stem,
                Domain = t.Domain,
                Difficulty = ctx.Difficulty.ToString(),
                Explanation = t.Explanation,
                ServiceTags = t.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                Options = options
            });
        }

        var warning = chosen.Count < ctx.Count
            ? $"Offline template bank exhausted for this filter ({chosen.Count}/{ctx.Count} produced). Configure an AI provider key for unlimited generation."
            : "Questions came from the local template bank, not an AI model. Set Ai:Provider to Gemini or Groq with a free API key for AI generation.";

        return Task.FromResult(new GenerationResult(chosen, Provider, "template-bank", warning));
    }

    private record Template(
        string CertCode,
        string Domain,
        string Stem,
        string[] Options,
        string[] Correct,
        string Explanation,
        string Tags);

    private static readonly Template[] Templates =
    [
        new("AIF-C01", "Fundamentals of Generative AI",
            "A developer needs a foundation model response to stay strictly grounded in a company handbook. Which combination is the lowest-effort way to achieve this on AWS?",
            [
                "Amazon Bedrock Knowledge Bases with an Amazon OpenSearch Serverless vector index",
                "Train a model from scratch on Amazon SageMaker AI",
                "Store the handbook in Amazon RDS and raise the model temperature",
                "Use Amazon Comprehend entity detection on each request"
            ],
            ["A"],
            "Knowledge Bases handle ingestion, chunking, embedding and retrieval for RAG with minimal code. Training from scratch is far more costly, and neither RDS plus temperature nor Comprehend grounds generation.",
            "Amazon Bedrock,OpenSearch Serverless"),

        new("AIF-C01", "Fundamentals of AI and ML",
            "Which metric is most appropriate for evaluating a binary classifier on a highly imbalanced fraud dataset?",
            [
                "Area under the precision-recall curve",
                "Plain accuracy",
                "Mean squared error",
                "R-squared"
            ],
            ["A"],
            "With heavy class imbalance accuracy is misleading because predicting the majority class scores well; precision-recall AUC focuses on the rare positive class. MSE and R-squared are regression metrics.",
            "Amazon SageMaker AI"),

        new("AIF-C01", "Applications of Foundation Models",
            "A prompt includes three worked examples before the real request. Which technique is being used?",
            [
                "Few-shot prompting",
                "Zero-shot prompting",
                "Fine-tuning",
                "Model distillation"
            ],
            ["A"],
            "Supplying examples inside the prompt is few-shot prompting. Zero-shot supplies none; fine-tuning and distillation change model weights rather than the prompt.",
            "Amazon Bedrock"),

        new("AIF-C01", "Guidelines for Responsible AI",
            "Which practices most directly reduce the risk of harmful bias in a deployed ML model? (Select TWO.)",
            [
                "Analysing feature and label imbalance with Amazon SageMaker Clarify",
                "Monitoring prediction distributions per demographic segment after launch",
                "Increasing the training batch size",
                "Switching the endpoint to a larger instance type",
                "Enabling S3 Transfer Acceleration on the training bucket"
            ],
            ["A", "B"],
            "Clarify surfaces pre-training bias metrics and post-training explainability, and segment-level monitoring catches drift in fairness after deployment. Batch size, instance type and transfer acceleration are performance concerns.",
            "Amazon SageMaker Clarify"),

        new("AIF-C01", "Security, Compliance, and Governance for AI Solutions",
            "Which service records every Amazon Bedrock API call for audit purposes?",
            [
                "AWS CloudTrail",
                "Amazon CloudWatch Metrics",
                "AWS Trusted Advisor",
                "AWS Systems Manager Inventory"
            ],
            ["A"],
            "CloudTrail captures management and data-plane API activity for auditing. CloudWatch Metrics stores numeric telemetry, not per-call audit records.",
            "AWS CloudTrail,Amazon Bedrock"),

        new("CLF-C02", "Cloud Concepts",
            "Which AWS Cloud benefit is described by replacing large upfront data-centre spending with usage-based charges?",
            [
                "Trading capital expense for variable expense",
                "Achieving unlimited durability",
                "Removing the need for security controls",
                "Guaranteeing lower latency worldwide"
            ],
            ["A"],
            "Paying only for consumed capacity converts capex into opex. Security remains a shared responsibility and latency still depends on Region and network design.",
            "AWS Cloud Adoption"),

        new("CLF-C02", "Cloud Technology and Services",
            "A company wants to run containers without managing servers. Which option meets this requirement?",
            [
                "Amazon ECS on AWS Fargate",
                "Amazon ECS on self-managed EC2 instances",
                "Amazon EC2 Auto Scaling groups",
                "AWS Batch on EC2 Spot"
            ],
            ["A"],
            "Fargate is a serverless compute engine for containers, so there are no instances to patch or scale. All other options require managing EC2 capacity.",
            "Amazon ECS,AWS Fargate"),

        new("CLF-C02", "Security and Compliance",
            "Which AWS service provides on-demand access to AWS compliance reports such as SOC and ISO certifications?",
            [
                "AWS Artifact",
                "AWS Audit Manager",
                "AWS Config",
                "Amazon Inspector"
            ],
            ["A"],
            "AWS Artifact is the self-service portal for AWS audit artifacts and agreements. Audit Manager assesses your own workloads against frameworks rather than publishing AWS reports.",
            "AWS Artifact"),

        new("CLF-C02", "Billing, Pricing, and Support",
            "Which tool lets you visualise and forecast AWS spend by service and tag?",
            [
                "AWS Cost Explorer",
                "AWS Budgets Actions",
                "AWS Pricing Calculator",
                "AWS License Manager"
            ],
            ["A"],
            "Cost Explorer visualises historical spend and forecasts future cost with filters and grouping. The Pricing Calculator estimates cost for architectures you have not deployed yet.",
            "AWS Cost Explorer"),

        new("SAA-C03", "Design Resilient Architectures",
            "An application must survive the loss of an entire Availability Zone with no data loss for its relational database. Which configuration meets this?",
            [
                "Amazon RDS Multi-AZ deployment with synchronous standby replication",
                "Amazon RDS single-AZ with automated daily snapshots",
                "Amazon RDS read replica in the same AZ",
                "Self-managed MySQL on one EC2 instance with EBS snapshots"
            ],
            ["A"],
            "Multi-AZ maintains a synchronous standby in another AZ and fails over automatically with no committed-data loss. Snapshots and same-AZ replicas cannot meet a zero-data-loss AZ failure requirement.",
            "Amazon RDS"),

        new("MLA-C01", "Deployment and Orchestration of ML Workflows",
            "A team must serve a model with unpredictable, bursty traffic and wants to avoid paying for idle capacity. Which SageMaker AI option fits best?",
            [
                "Serverless inference endpoints",
                "Real-time endpoint on a fixed ml.p4d instance",
                "Batch transform on a schedule",
                "Asynchronous inference with a provisioned fleet"
            ],
            ["A"],
            "Serverless inference scales to zero between requests, so idle time is not billed. A fixed instance bills continuously and batch transform cannot serve interactive requests.",
            "Amazon SageMaker AI"),

        new("MLA-C01", "ML Solution Monitoring, Maintenance, and Security",
            "Which two SageMaker AI features help detect that live inference data has drifted from the training distribution?",
            [
                "SageMaker Model Monitor data-quality monitoring jobs",
                "Baseline statistics and constraints captured from the training dataset",
                "SageMaker Feature Store online store TTL",
                "Increasing the endpoint instance count",
                "Enabling multi-model endpoints"
            ],
            ["A", "B"],
            "Model Monitor compares captured inference data against a baseline of statistics and constraints generated from training data to flag drift. Scaling and multi-model endpoints affect capacity, not drift detection.",
            "Amazon SageMaker Model Monitor")
    ];
}
