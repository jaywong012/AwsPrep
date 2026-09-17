using System.Text.RegularExpressions;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Canonical AWS service and feature names, used to tag questions with the topics they
/// actually test. The list is the vocabulary the reference bank draws on, so a topic that
/// appears here is a topic the real exam is known to ask about.
/// </summary>
public static partial class AwsServiceVocabulary
{
    /// <summary>Longest names first, so "Amazon S3 Glacier" wins over "Amazon S3".</summary>
    public static readonly string[] Names =
    [
        "AWS Identity and Access Management", "Amazon Elastic Block Store", "Amazon Elastic File System",
        "Amazon Elastic Container Service", "Amazon Elastic Kubernetes Service", "Amazon Simple Queue Service",
        "Amazon Simple Notification Service", "Amazon Simple Email Service", "Amazon Virtual Private Cloud",
        "Amazon Quantum Ledger Database", "AWS Cloud Adoption Framework", "AWS Well-Architected Framework",
        "Amazon CloudWatch Logs", "Amazon S3 Glacier", "AWS Key Management Service", "AWS Security Token Service",
        "AWS Cloud Development Kit", "Amazon Machine Image", "AWS Professional Services", "AWS Managed Services",
        "AWS Personal Health Dashboard", "AWS Health Dashboard", "AWS Pricing Calculator", "AWS Cost Explorer",
        "AWS Cost and Usage Report", "AWS Billing Conductor", "AWS License Manager", "AWS Compute Optimizer",
        "AWS Trusted Advisor", "AWS Service Catalog", "AWS Systems Manager", "AWS Secrets Manager",
        "AWS Certificate Manager", "AWS Firewall Manager", "AWS Audit Manager", "AWS Control Tower",
        "AWS Organizations", "AWS Resource Access Manager", "AWS Resource Explorer", "AWS Launch Wizard",
        "AWS Storage Gateway", "AWS Direct Connect", "AWS Transit Gateway", "AWS PrivateLink",
        "AWS Site-to-Site VPN", "AWS Client VPN", "AWS Global Accelerator", "AWS Local Zones",
        "AWS Wavelength", "AWS Outposts", "AWS Snowball Edge", "AWS Snowmobile", "AWS Snowcone",
        "AWS DataSync", "AWS Elastic Beanstalk", "AWS CloudFormation", "AWS CodePipeline", "AWS CodeBuild",
        "AWS CodeDeploy", "AWS CodeCommit", "AWS CodeArtifact", "AWS CodeStar", "Amazon CodeGuru",
        "AWS Step Functions", "AWS Lambda", "AWS Fargate", "AWS Batch", "AWS Amplify", "AWS App Runner",
        "AWS AppSync", "AWS Glue", "AWS Lake Formation", "AWS Data Exchange", "AWS Database Migration Service",
        "AWS Application Migration Service", "AWS Migration Hub", "AWS Backup", "AWS Elastic Disaster Recovery",
        "AWS CloudTrail", "AWS Config", "AWS CloudHSM", "AWS CloudShell", "AWS Cloud Map", "AWS Cloud9",
        "AWS X-Ray", "AWS Shield", "AWS WAF", "AWS Security Hub", "AWS Artifact", "AWS Marketplace",
        "AWS Budgets", "AWS Free Tier", "AWS Support", "AWS Application Discovery Service",
        "Amazon API Gateway", "Amazon AppStream 2.0", "Amazon Athena", "Amazon Aurora", "Amazon Bedrock",
        "Amazon Chime", "Amazon CloudFront", "Amazon CloudWatch", "Amazon Cognito", "Amazon Comprehend",
        "Amazon Connect", "Amazon Detective", "Amazon DocumentDB", "Amazon DynamoDB", "Amazon EC2 Auto Scaling",
        "Amazon EC2", "Amazon ECS", "Amazon EFS", "Amazon EKS", "Amazon ElastiCache", "Amazon EMR",
        "Amazon EventBridge", "Amazon FSx", "Amazon GuardDuty", "Amazon Inspector", "Amazon Kendra",
        "Amazon Keyspaces", "Amazon Kinesis Data Firehose", "Amazon Kinesis Data Streams", "Amazon Kinesis",
        "Amazon Lex", "Amazon Lightsail", "Amazon Macie", "Amazon Managed Grafana", "Amazon MQ",
        "Amazon Neptune", "Amazon OpenSearch Service", "Amazon Personalize", "Amazon Polly",
        "Amazon QuickSight", "Amazon RDS", "Amazon Redshift", "Amazon Rekognition", "Amazon Route 53",
        "Amazon S3", "Amazon SageMaker AI", "Amazon SageMaker", "Amazon SES", "Amazon SNS", "Amazon SQS",
        "Amazon Textract", "Amazon Timestream", "Amazon Transcribe", "Amazon Translate", "Amazon VPC",
        "Amazon WorkDocs", "Amazon WorkSpaces", "Elastic Load Balancing", "Application Load Balancer",
        "Network Load Balancer", "AWS CLI", "AWS SDK", "AWS Management Console",
        "Availability Zone", "AWS Region", "Edge location", "Shared responsibility model",
        "Savings Plans", "Reserved Instances", "Spot Instances", "On-Demand Instances", "Dedicated Hosts",
        "Security group", "Network ACL", "Internet gateway", "NAT gateway", "VPC endpoint",
        "Multi-factor authentication", "IAM role", "IAM policy", "S3 Versioning", "EBS snapshot",

        // Abbreviations the exams use as often as the full names, and which appear on their own
        // in short options ("AWS KMS", "Amazon EBS"), so an item is not left untagged.
        "AWS KMS", "AWS IAM", "AWS STS", "AWS ACM", "AWS RAM", "AWS CAF", "AWS DMS", "AWS SSO",
        "Amazon EBS", "Amazon QLDB", "Amazon MSK", "Amazon ECR",
        "AWS Knowledge Center", "AWS re:Post", "IAM credential report",
    ];

    private static readonly Regex[] Patterns = Names
        .OrderByDescending(n => n.Length)
        .Select(n => new Regex(@"\b" + Regex.Escape(n) + @"s?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    private static readonly string[] Ordered = [.. Names.OrderByDescending(n => n.Length)];

    /// <summary>
    /// Names mentioned in <paramref name="text"/>, longest match first and each reported once.
    /// A match is removed from the text so "Amazon S3 Glacier" does not also report "Amazon S3".
    /// </summary>
    public static List<string> Extract(string text, int max = 4)
    {
        var remaining = text;
        var found = new List<string>();

        for (var i = 0; i < Patterns.Length && found.Count < max; i++)
        {
            if (!Patterns[i].IsMatch(remaining)) continue;
            found.Add(Ordered[i]);
            remaining = Patterns[i].Replace(remaining, " ");
        }

        return found;
    }
}
