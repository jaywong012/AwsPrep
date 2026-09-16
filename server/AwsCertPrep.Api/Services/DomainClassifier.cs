using System.Text.RegularExpressions;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Assigns a reference item to one of a certification's official exam domains.
///
/// The source banks do not label domains, and the domain breakdown is what drives the
/// per-domain accuracy report and the readiness projection, so items are classified from the
/// language they use. Two rules keep it honest:
///   - only the stem and the keyed (correct) options are scored, because distractors routinely
///     name services from other domains and would otherwise decide the label;
///   - keywords match on a left word boundary, so "encrypt" still matches "encryption" while
///     "sts" no longer matches "costs".
/// An item that matches nothing falls back to the broad services domain.
/// </summary>
public static class DomainClassifier
{
    /// <summary>A domain and the language that signals it. Weight breaks ties between domains.</summary>
    private record DomainRule(string DomainName, int Weight, string[] Keywords);

    /// <summary>The stem states what the item is about; a keyed option only hints at it.</summary>
    private const int StemWeight = 3;

    /// <summary>
    /// Words that read like a domain keyword but are really the deciding qualifier, and appear
    /// in items from every domain ("the MOST cost-effective way to query Amazon S3" is a storage
    /// and analytics item, not a billing item). They still count, but at the weight of a hint
    /// rather than of a subject, so a genuine billing term in the same item outvotes them.
    /// </summary>
    private static readonly HashSet<string> QualifierKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "cost-effective", "cost optimization" };

    private static readonly Dictionary<string, DomainRule[]> RulesByCertification = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLF-C02"] =
        [
            new("Security and Compliance", 3,
            [
                "shared responsibility", "identity and access management", "iam", "mfa", "multi-factor",
                "least privilege", "principle of least", "encrypt", "kms", "key management service", "cloudhsm",
                "certificate manager", "guardduty", "inspector", "macie", "shield", "waf",
                "web application firewall", "security hub", "detective", "aws artifact", "audit manager",
                "compliance", "credential report", "access analyzer", "root user", "access key",
                "secrets manager", "security group", "network acl", "penetration test", "firewall manager",
                "cognito", "security token service", "sts", "vulnerabilit", "ddos", "patch", "permission",
                "policy", "authenticat", "authoriz", "threat", "malicious", "cloudtrail", "data privacy",
                "gdpr", "hipaa", "pci dss", "soc 1", "security best practice", "assume a role", "iam role",
                "sensitive data", "unauthorized", "audit", "credential", "password", "access control",
                "block network traffic", "given access",
            ]),

            new("Billing, Pricing, and Support", 3,
            [
                "pricing", "cost explorer", "budget", "billing", "invoice", "savings plan",
                "reserved instance", "spot instance", "free tier", "total cost of ownership",
                "consolidated billing", "cost allocation tag", "trusted advisor", "support plan",
                "basic support", "developer support", "business support", "enterprise support",
                "technical account manager", "cost and usage report", "pricing calculator", "license manager",
                "marketplace", "billing conductor", "no additional charge", "no charge", "at no cost",
                "no cost", "cost-effective", "capital expense", "operational expense", "no additional cost",
                "no upfront", "upfront payment", "per-hour", "cost optimization", "rightsizing",
                "estimate the cost", "cost estimate", "tco",
            ]),

            new("Cloud Concepts", 3,
            [
                "well-architected", "cloud adoption framework", "aws caf", "perspective", "agility",
                "elasticity", "scalability", "economies of scale", "advantage that", "advantages of cloud",
                "migration strategy", "rehost", "replatform", "refactor", "retire", "cloud value framework",
                "undifferentiated", "high availability", "fault tolerance", "loose coupl", "decoupl",
                "design principle", "pillar", "availability zone", "edge location", "global infrastructure",
                "on-premises data center", "capital expenditure", "operational excellence", "sustainability",
                "reliability", "performance efficiency", "disaster recovery", "business continuity",
                "scale out", "scale up", "cloud computing", "agile", "stop guessing capacity",
                "go global in minutes", "trade capital expense", "vertical scaling", "horizontal scaling",
                "single point of failure",
            ]),

            new("Cloud Technology and Services", 1,
            [
                "ec2", "amazon s3", "lambda", "dynamodb", "amazon rds", "aurora", "vpc", "route 53",
                "cloudfront", "ebs", "elastic block", "efs", "elastic file", "fsx", "sqs", "simple queue",
                "sns", "simple notification", "ses", "simple email", "eventbridge", "cloudformation",
                "beanstalk", "ecs", "elastic container", "eks", "kubernetes", "fargate", "snowball",
                "snowmobile", "storage gateway", "direct connect", "transit gateway", "privatelink",
                "outposts", "local zones", "wavelength", "workspaces", "appstream", "amazon connect",
                "athena", "redshift", "emr", "glue", "kinesis", "quicksight", "sagemaker", "cloudwatch",
                "aws config", "systems manager", "organizations", "control tower", "service catalog",
                "auto scaling", "load balanc", "api gateway", "step functions", "aws cli", "aws sdk",
                "cloud development kit", "codepipeline", "codebuild", "codedeploy", "codestar", "codeguru",
                "documentdb", "neptune", "qldb", "quantum ledger", "elasticache", "datasync",
                "data transfer", "internet gateway", "nat gateway", "site-to-site vpn", "client vpn",
                "aws vpn", "health dashboard", "resource explorer", "launch wizard", "managed services",
                "professional services", "chatbot", "knowledge center", "versioning", "machine image",
                "ami", "snapshot", "instance type", "serverless", "container", "batch", "personalize",
                "comprehend", "rekognition", "lex", "polly", "translate", "forecast", "kendra", "amazon mq",
                "lightsail", "global accelerator", "app runner", "amplify", "cloud9", "x-ray", "cloudshell",
                "cloud map", "compute optimizer",
            ]),
        ],
    };

    private static readonly Dictionary<string, Regex> PatternCache = [];
    private static readonly Lock CacheLock = new();

    /// <summary>
    /// The domain an item belongs to, or null when the certification has no classification rules.
    /// </summary>
    public static string? Classify(string certificationCode, string stem, string keyedOptions)
    {
        if (!RulesByCertification.TryGetValue(certificationCode, out var rules)) return null;

        var stemText = stem.ToLowerInvariant();
        var keyedText = keyedOptions.ToLowerInvariant();

        string? best = null;
        var bestScore = 0;

        foreach (var rule in rules)
        {
            var stemHits = rule.Keywords.Count(k => !QualifierKeywords.Contains(k) && Matches(k, stemText));
            var keyedHits = rule.Keywords.Count(k =>
                !QualifierKeywords.Contains(k) && Matches(k, keyedText) && !Matches(k, stemText));
            var qualifierHits = rule.Keywords.Count(k =>
                QualifierKeywords.Contains(k) && (Matches(k, stemText) || Matches(k, keyedText)));

            var score = rule.Weight * (StemWeight * stemHits + keyedHits + qualifierHits);

            if (score > bestScore)
            {
                bestScore = score;
                best = rule.DomainName;
            }
        }

        return best ?? rules[^1].DomainName;
    }

    private static bool Matches(string keyword, string text)
    {
        Regex pattern;
        lock (CacheLock)
        {
            if (!PatternCache.TryGetValue(keyword, out pattern!))
                pattern = PatternCache[keyword] = new Regex(@"\b" + Regex.Escape(keyword), RegexOptions.Compiled);
        }

        return pattern.IsMatch(text);
    }
}
