namespace AwsCertPrep.Api.Data;

/// <summary>
/// The seeded study curriculum.
///
/// Provenance, stated plainly because a learner bets an exam on it: this file is hand-written
/// against the CLF-C02 exam guide and general AWS knowledge, and every DocsUrl and PricingUrl has
/// been checked to resolve. It is NOT a transcription of those pages, and no claim here has been
/// diffed against the page it links to - the link is where to confirm, not the source it came
/// from. Treat a correction to this file as a bug fix, and prefer the linked page over this text
/// wherever the two disagree.
///
/// What it does buy: these fields are stable and never model-generated, so the service names,
/// what each is for and how it is charged do not drift between readings the way the generated
/// LessonContent can. The generated depth is written against these facts, not the other way round.
///
/// <para>
/// ServiceTags is a comma-separated list of every name a question might carry for this topic - the
/// full name, the abbreviation, and the sub-features that belong to it. Questions are tagged from
/// free text, so "Amazon EBS" and "Amazon Elastic Block Store" both occur in the bank; listing the
/// aliases here is what lets the lessons list order itself by the services a learner gets wrong.
/// </para>
/// <para>
/// IsCore marks the topics the CLF-C02 reference bank tests most often, so a learner with no
/// answer history still starts on the services the exam really leans on.
/// </para>
/// </summary>
public static class LessonCatalog
{
    public record CatalogTopic(
        string Domain,
        string Category,
        string Slug,
        string Title,
        string? ServiceTags,
        string Purpose,
        string PricingModel,
        string DocsUrl,
        string? PricingUrl,
        bool IsCore);

    private const string Concepts = "Cloud Concepts";
    private const string Security = "Security and Compliance";
    private const string Tech = "Cloud Technology and Services";
    private const string Billing = "Billing, Pricing, and Support";

    public static readonly Dictionary<string, CatalogTopic[]> ByCertification = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLF-C02"] =
        [
            // ---------- Domain 1: Cloud Concepts ----------
            new(Concepts, "Cloud fundamentals", "benefits-of-the-aws-cloud", "Benefits of the AWS Cloud", null,
                "The six advantages of cloud computing - trade fixed (capital) expense for variable expense, benefit from massive economies of scale, stop guessing capacity, increase speed and agility, stop spending money running data centers, and go global in minutes.",
                "Not a service. This is the value framework the exam asks you to recognise from a description.",
                "https://aws.amazon.com/what-is-cloud-computing/", null, true),

            new(Concepts, "Cloud fundamentals", "cloud-deployment-models", "Cloud deployment models", null,
                "Cloud, hybrid and on-premises deployment models, and when an organisation would choose each.",
                "Not a service. The cost differences between the models are what the exam tests.",
                "https://docs.aws.amazon.com/whitepapers/latest/aws-overview/types-of-cloud-computing.html", null, false),

            new(Concepts, "Global infrastructure", "aws-global-infrastructure", "Regions, Availability Zones, and edge locations", "AWS Region,Availability Zone,Edge location",
                "The physical layout of AWS: Regions are separate geographic areas, Availability Zones are isolated groups of data centres inside a Region, and edge locations cache content close to users.",
                "No charge for the infrastructure itself; data transfer between Regions and between Availability Zones is billed.",
                "https://aws.amazon.com/about-aws/global-infrastructure/", null, true),

            new(Concepts, "Architecture", "well-architected-framework", "AWS Well-Architected Framework", "AWS Well-Architected Framework",
                "Six pillars for reviewing a workload: operational excellence, security, reliability, performance efficiency, cost optimization, and sustainability.",
                "The framework and the AWS Well-Architected Tool are free to use.",
                "https://docs.aws.amazon.com/wellarchitected/latest/framework/welcome.html", null, true),

            new(Concepts, "Architecture", "cloud-adoption-framework", "AWS Cloud Adoption Framework (AWS CAF)", "AWS Cloud Adoption Framework,AWS CAF,AWS Cloud Adoption",
                "Six perspectives that organise the work of moving to the cloud: business, people, governance, platform, security, and operations.",
                "Not a service - guidance only, at no charge.",
                "https://docs.aws.amazon.com/whitepapers/latest/overview-aws-cloud-adoption-framework/welcome.html", null, true),

            new(Concepts, "Architecture", "migration-strategies", "The migration strategies (the 7 Rs)", null,
                "Rehost, replatform, refactor, repurchase, retire, retain and relocate - the seven ways an existing workload can be moved to AWS.",
                "Not a service. The exam asks which strategy a described migration is using.",
                "https://docs.aws.amazon.com/prescriptive-guidance/latest/large-migration-guide/migration-strategies.html", null, false),

            new(Concepts, "Design principles", "elasticity-and-scalability", "Elasticity, scalability, and agility", null,
                "Elasticity is matching capacity to demand automatically; scalability is the ability to grow; agility is how quickly new resources can be brought online.",
                "Not a service. These are the words the exam uses to describe a benefit you must name.",
                "https://docs.aws.amazon.com/wellarchitected/latest/framework/general-design-principles.html", null, true),

            new(Concepts, "Design principles", "high-availability-and-fault-tolerance", "High availability, fault tolerance, and disaster recovery", null,
                "Designing across Availability Zones so a single failure does not take the workload down, and planning recovery objectives for when it does: RTO is the maximum acceptable delay before service is restored, RPO the maximum acceptable amount of data lost.",
                "Not a service. Cost rises with the number of redundant components you run.",
                "https://docs.aws.amazon.com/wellarchitected/latest/reliability-pillar/disaster-recovery-dr-objectives.html", null, true),

            new(Concepts, "Design principles", "loose-coupling-and-decoupling", "Loose coupling and decoupled architectures", null,
                "Putting a queue, stream or workflow between components so a failure in one is isolated from the others. AWS names queuing systems, streaming systems, workflows and load balancers as the loosely coupled dependencies.",
                "Not a service. Cost comes from the queue or topic you decouple with.",
                "https://docs.aws.amazon.com/wellarchitected/latest/reliability-pillar/rel_prevent_interaction_failure_loosely_coupled_system.html", null, false),

            // ---------- Domain 2: Security and Compliance ----------
            new(Security, "Shared responsibility", "shared-responsibility-model", "The AWS shared responsibility model", "Shared responsibility model",
                "AWS is responsible for security OF the cloud (hardware, the global infrastructure, managed service software); the customer is responsible for security IN the cloud (their data, IAM, patching guest operating systems, firewall configuration).",
                "Not a service. The split shifts by service type, which is exactly what the exam probes.",
                "https://aws.amazon.com/compliance/shared-responsibility-model/", null, true),

            new(Security, "Identity", "aws-iam", "AWS Identity and Access Management (IAM)", "AWS Identity and Access Management,AWS IAM,IAM policy",
                "Controls who is authenticated and what they are authorised to do, through users, groups, roles and policies.",
                "No additional charge. IAM is free with every AWS account.",
                "https://docs.aws.amazon.com/IAM/latest/UserGuide/introduction.html", null, true),

            new(Security, "Identity", "iam-roles-and-sts", "IAM roles and AWS STS", "AWS Security Token Service,AWS STS,IAM role",
                "A role is a set of permissions that can be assumed temporarily, with AWS STS issuing the short-lived credentials - the correct way to give an EC2 instance or another account access without long-lived keys.",
                "No additional charge.",
                "https://docs.aws.amazon.com/IAM/latest/UserGuide/id_roles.html", null, true),

            new(Security, "Identity", "root-user-and-mfa", "The root user and multi-factor authentication", "Multi-factor authentication",
                "The root user has unrestricted access and should be locked down: enable MFA, do not create access keys for it, and use an IAM identity for day-to-day work.",
                "No additional charge; virtual MFA applications are free.",
                "https://docs.aws.amazon.com/IAM/latest/UserGuide/id_root-user.html", null, true),

            new(Security, "Identity", "amazon-cognito", "Amazon Cognito", "Amazon Cognito",
                "Sign-up, sign-in and access control for your own web and mobile application users - application users, as opposed to IAM which governs AWS account access.",
                "Priced per monthly active user, with a free tier.",
                "https://docs.aws.amazon.com/cognito/latest/developerguide/what-is-amazon-cognito.html",
                "https://aws.amazon.com/cognito/pricing/", false),

            new(Security, "Encryption", "aws-kms", "AWS Key Management Service (AWS KMS)", "AWS Key Management Service,AWS KMS",
                "Creates and controls the encryption keys used to encrypt data across AWS services, with every use logged to AWS CloudTrail.",
                "Per customer managed key per month, plus per API request. AWS managed keys are free.",
                "https://docs.aws.amazon.com/kms/latest/developerguide/overview.html",
                "https://aws.amazon.com/kms/pricing/", true),

            new(Security, "Encryption", "aws-secrets-manager", "AWS Secrets Manager", "AWS Secrets Manager",
                "Stores database credentials, API keys and other secrets, and can rotate them automatically.",
                "Per secret per month, plus per 10,000 API calls.",
                "https://docs.aws.amazon.com/secretsmanager/latest/userguide/intro.html",
                "https://aws.amazon.com/secrets-manager/pricing/", true),

            new(Security, "Encryption", "aws-certificate-manager", "AWS Certificate Manager (ACM)", "AWS Certificate Manager,AWS ACM",
                "Provisions, manages and renews SSL/TLS certificates for use with AWS services such as Elastic Load Balancing and Amazon CloudFront.",
                "Public certificates used with integrated AWS services are free; AWS Private CA is charged.",
                "https://docs.aws.amazon.com/acm/latest/userguide/acm-overview.html",
                "https://aws.amazon.com/certificate-manager/pricing/", false),

            new(Security, "Detection", "amazon-guardduty", "Amazon GuardDuty", "Amazon GuardDuty",
                "Continuously monitors CloudTrail, VPC flow logs and DNS logs for malicious or unauthorised behaviour - a threat detection service.",
                "Per GB of logs and events analysed, with a 30-day free trial.",
                "https://docs.aws.amazon.com/guardduty/latest/ug/what-is-guardduty.html",
                "https://aws.amazon.com/guardduty/pricing/", true),

            new(Security, "Detection", "amazon-inspector", "Amazon Inspector", "Amazon Inspector",
                "Automated vulnerability scanning of EC2 instances, container images and Lambda functions, reporting software vulnerabilities and unintended network exposure.",
                "Per instance or per container image scanned, per month.",
                "https://docs.aws.amazon.com/inspector/latest/user/what-is-inspector.html",
                "https://aws.amazon.com/inspector/pricing/", true),

            new(Security, "Detection", "amazon-macie", "Amazon Macie", "Amazon Macie",
                "Uses machine learning to discover and classify sensitive data, such as personally identifiable information, stored in Amazon S3.",
                "Per S3 bucket evaluated and per GB of data inspected.",
                "https://docs.aws.amazon.com/macie/latest/user/what-is-macie.html",
                "https://aws.amazon.com/macie/pricing/", false),

            new(Security, "Detection", "aws-security-hub", "AWS Security Hub", "AWS Security Hub",
                "Aggregates findings from GuardDuty, Inspector, Macie and partner tools into one view, and runs automated best-practice checks against standards such as CIS, PCI DSS and NIST. Now branded AWS Security Hub CSPM; the exam still calls it AWS Security Hub.",
                "Per security check and per finding ingested.",
                "https://docs.aws.amazon.com/securityhub/latest/userguide/what-is-securityhub.html",
                "https://aws.amazon.com/security-hub/pricing/", false),

            new(Security, "Network protection", "aws-shield", "AWS Shield", "AWS Shield",
                "DDoS protection. Shield Standard is automatic and free for all customers; Shield Advanced adds higher-level protections and access to a response team.",
                "Standard is free. Advanced is a monthly subscription with a 1-year commitment.",
                "https://docs.aws.amazon.com/waf/latest/developerguide/ddos-overview.html",
                "https://aws.amazon.com/shield/pricing/", true),

            new(Security, "Network protection", "aws-waf", "AWS WAF", "AWS WAF",
                "A web application firewall that filters HTTP and HTTPS requests by rule - blocking SQL injection, cross-site scripting and unwanted traffic patterns.",
                "Per web ACL per month, per rule, and per million requests.",
                "https://docs.aws.amazon.com/waf/latest/developerguide/what-is-aws-waf.html",
                "https://aws.amazon.com/waf/pricing/", true),

            new(Security, "Network protection", "security-groups-and-nacls", "Security groups and network ACLs", "Security group,Network ACL",
                "Security groups are stateful firewalls attached to an instance and hold allow rules only; network ACLs are stateless, attached to a subnet, and support both allow and deny rules.",
                "No additional charge for either.",
                "https://docs.aws.amazon.com/vpc/latest/userguide/infrastructure-security.html", null, true),

            new(Security, "Governance", "aws-cloudtrail", "AWS CloudTrail", "AWS CloudTrail",
                "Records API calls and account activity - who did what, when, and from where. The service to name whenever a question is about auditing actions.",
                "Event history shows the last 90 days of management events at no CloudTrail charge, and one copy of ongoing management events can be delivered to S3 free - you still pay S3 storage. Data events and extra trails are charged.",
                "https://docs.aws.amazon.com/awscloudtrail/latest/userguide/cloudtrail-user-guide.html",
                "https://aws.amazon.com/cloudtrail/pricing/", true),

            new(Security, "Governance", "aws-config", "AWS Config", "AWS Config",
                "Records the configuration of your resources over time and evaluates them against rules - the service for \"is this resource configured the way it should be\".",
                "Per configuration item recorded and per rule evaluation.",
                "https://docs.aws.amazon.com/config/latest/developerguide/WhatIsConfig.html",
                "https://aws.amazon.com/config/pricing/", true),

            new(Security, "Compliance", "aws-artifact", "AWS Artifact", "AWS Artifact",
                "Self-service portal for AWS compliance reports (SOC, ISO, PCI) and agreements. The answer whenever a question asks where to download AWS audit reports.",
                "No charge.",
                "https://docs.aws.amazon.com/artifact/latest/ug/what-is-aws-artifact.html", null, true),

            new(Security, "Compliance", "aws-audit-manager", "AWS Audit Manager", "AWS Audit Manager",
                "Continuously collects evidence from your own AWS usage and maps it to compliance frameworks - assessing your workloads, not AWS itself, which is the distinction from AWS Artifact. NOTE, AWS Audit Manager is no longer open to new customers, though it is still examinable.",
                "Per resource assessment.",
                "https://docs.aws.amazon.com/audit-manager/latest/userguide/what-is.html",
                "https://aws.amazon.com/audit-manager/pricing/", false),

            // ---------- Domain 3: Cloud Technology and Services ----------
            new(Tech, "Compute", "amazon-ec2", "Amazon EC2", "Amazon EC2,Amazon Machine Image",
                "Resizable virtual servers in the cloud with full control of the operating system - the baseline compute service every other option is compared against.",
                "Per second or per hour by instance type, plus attached storage and data transfer out.",
                "https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/concepts.html",
                "https://aws.amazon.com/ec2/pricing/", true),

            new(Tech, "Compute", "amazon-ec2-auto-scaling", "Amazon EC2 Auto Scaling", "Amazon EC2 Auto Scaling",
                "Adds and removes EC2 instances automatically to match demand, and replaces unhealthy instances.",
                "No additional charge - you pay only for the instances it launches.",
                "https://docs.aws.amazon.com/autoscaling/ec2/userguide/what-is-amazon-ec2-auto-scaling.html", null, true),

            new(Tech, "Compute", "elastic-load-balancing", "Elastic Load Balancing", "Elastic Load Balancing,Application Load Balancer,Network Load Balancer",
                "Distributes incoming traffic across multiple targets in multiple Availability Zones, and stops sending traffic to unhealthy ones.",
                "Per load balancer hour plus capacity units consumed.",
                "https://docs.aws.amazon.com/elasticloadbalancing/latest/userguide/what-is-load-balancing.html",
                "https://aws.amazon.com/elasticloadbalancing/pricing/", true),

            new(Tech, "Compute", "aws-lambda", "AWS Lambda", "AWS Lambda",
                "Runs code in response to events without provisioning or managing servers, scaling automatically from zero.",
                "Per request and per GB-second of compute, with a perpetual free tier.",
                "https://docs.aws.amazon.com/lambda/latest/dg/welcome.html",
                "https://aws.amazon.com/lambda/pricing/", true),

            new(Tech, "Containers", "containers-on-aws", "Amazon ECS, Amazon EKS, and AWS Fargate", "AWS Fargate,Amazon ECS,Amazon EKS,Amazon Elastic Container Service,Amazon Elastic Kubernetes Service,Amazon ECR",
                "ECS and EKS orchestrate containers; Fargate is the serverless capacity they can run on, removing the EC2 instances you would otherwise manage.",
                "EKS charges per cluster hour; Fargate charges per vCPU and GB-second of the tasks you run.",
                "https://docs.aws.amazon.com/AmazonECS/latest/developerguide/Welcome.html",
                "https://aws.amazon.com/fargate/pricing/", true),

            new(Tech, "Compute", "aws-elastic-beanstalk", "AWS Elastic Beanstalk", "AWS Elastic Beanstalk",
                "Deploys and scales a web application from uploaded code, provisioning the EC2 instances, load balancer and scaling for you while leaving them visible and adjustable.",
                "No additional charge - you pay for the resources it creates.",
                "https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/Welcome.html", null, true),

            new(Tech, "Compute", "aws-batch", "AWS Batch", "AWS Batch",
                "Runs batch computing jobs at any scale, provisioning the right amount and type of compute for the queued work.",
                "No additional charge - you pay for the EC2 or Fargate resources consumed.",
                "https://docs.aws.amazon.com/batch/latest/userguide/what-is-batch.html", null, false),

            new(Tech, "Storage", "amazon-s3", "Amazon S3", "Amazon S3,S3 Versioning",
                "Object storage for any amount of data, with eleven nines of durability, accessed over HTTP rather than mounted as a drive.",
                "Per GB-month by storage class, plus requests, retrievals and data transfer out.",
                "https://docs.aws.amazon.com/AmazonS3/latest/userguide/Welcome.html",
                "https://aws.amazon.com/s3/pricing/", true),

            new(Tech, "Storage", "s3-storage-classes", "S3 storage classes and lifecycle policies", "Amazon S3 Glacier",
                "Standard, Intelligent-Tiering, Standard-IA, One Zone-IA, Glacier Instant Retrieval, Glacier Flexible Retrieval and Glacier Deep Archive, with lifecycle rules to move objects between them as they age. S3 Express One Zone also exists for single-digit-millisecond access, but the exam concentrates on the seven above.",
                "Cost per GB falls as retrieval time rises; the archive classes add a retrieval charge.",
                "https://docs.aws.amazon.com/AmazonS3/latest/userguide/storage-class-intro.html",
                "https://aws.amazon.com/s3/pricing/", true),

            new(Tech, "Storage", "amazon-ebs", "Amazon Elastic Block Store (Amazon EBS)", "Amazon EBS,Amazon Elastic Block Store,EBS snapshot",
                "Block storage volumes attached to a single EC2 instance in one Availability Zone - the instance's disk.",
                "Per GB-month provisioned (not used), plus IOPS on some volume types, plus snapshots.",
                "https://docs.aws.amazon.com/ebs/latest/userguide/what-is-ebs.html",
                "https://aws.amazon.com/ebs/pricing/", true),

            new(Tech, "Storage", "amazon-efs", "Amazon Elastic File System (Amazon EFS)", "Amazon EFS,Amazon Elastic File System",
                "A shared file system that many EC2 instances across multiple Availability Zones can mount at the same time, growing and shrinking automatically.",
                "Per GB-month of data stored, by storage class; nothing to provision.",
                "https://docs.aws.amazon.com/efs/latest/ug/whatisefs.html",
                "https://aws.amazon.com/efs/pricing/", true),

            new(Tech, "Storage", "aws-storage-gateway", "AWS Storage Gateway", "AWS Storage Gateway",
                "Connects an on-premises environment to AWS storage while keeping frequently used data cached locally - the answer for extending on-premises file storage without losing local performance.",
                "Per GB stored in AWS, plus gateway usage and data transfer. The software appliance is downloaded and run on your own hardware or as a VM.",
                "https://docs.aws.amazon.com/storagegateway/latest/userguide/WhatIsStorageGateway.html",
                "https://aws.amazon.com/storagegateway/pricing/", true),

            new(Tech, "Storage", "aws-snow-family", "The AWS Snow Family", "AWS Snowball Edge,AWS Snowcone,AWS Snowmobile",
                "Physical devices (Snowcone, Snowball Edge) shipped to you for transferring large data sets into AWS when the network would take too long.",
                "Per job plus per day of device use. Transferring data INTO Amazon S3 is free; data out is charged.",
                "https://docs.aws.amazon.com/snowball/latest/developer-guide/whatisedge.html",
                "https://aws.amazon.com/snowball/pricing/", true),

            new(Tech, "Storage", "aws-backup", "AWS Backup", "AWS Backup",
                "Centralises and automates backup policies across EBS, RDS, DynamoDB, EFS and more from a single place.",
                "Per GB of backup storage and per restore.",
                "https://docs.aws.amazon.com/aws-backup/latest/devguide/whatisbackup.html",
                "https://aws.amazon.com/backup/pricing/", false),

            new(Tech, "Databases", "amazon-rds", "Amazon RDS", "Amazon RDS",
                "Managed relational databases (MySQL, PostgreSQL, MariaDB, Oracle, SQL Server and IBM Db2) where AWS handles patching, backups, automatic failure detection and recovery.",
                "Per instance hour plus storage and backups; Multi-AZ roughly doubles the instance cost.",
                "https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/Welcome.html",
                "https://aws.amazon.com/rds/pricing/", true),

            new(Tech, "Databases", "amazon-aurora", "Amazon Aurora", "Amazon Aurora",
                "A MySQL- and PostgreSQL-compatible relational database built for the cloud, with storage that keeps six copies of your data across three Availability Zones automatically.",
                "Per instance hour plus per GB-month of storage and per million I/O requests; Serverless v2 bills capacity units.",
                "https://docs.aws.amazon.com/AmazonRDS/latest/AuroraUserGuide/CHAP_AuroraOverview.html",
                "https://aws.amazon.com/rds/aurora/pricing/", true),

            new(Tech, "Databases", "amazon-dynamodb", "Amazon DynamoDB", "Amazon DynamoDB",
                "A serverless key-value and document NoSQL database with single-digit millisecond latency at any scale, and no servers to manage.",
                "On-demand per request, or provisioned capacity per hour, plus storage.",
                "https://docs.aws.amazon.com/amazondynamodb/latest/developerguide/Introduction.html",
                "https://aws.amazon.com/dynamodb/pricing/", true),

            new(Tech, "Databases", "amazon-elasticache", "Amazon ElastiCache", "Amazon ElastiCache",
                "Managed in-memory caching (Valkey, Memcached or Redis OSS) placed in front of a database to cut read latency and load.",
                "Per node hour, or per data processed on the serverless option.",
                "https://docs.aws.amazon.com/AmazonElastiCache/latest/dg/WhatIs.html",
                "https://aws.amazon.com/elasticache/pricing/", false),

            new(Tech, "Databases", "amazon-redshift", "Amazon Redshift", "Amazon Redshift",
                "A petabyte-scale data warehouse for analytics and complex queries over structured data - OLAP, not the transactional store your application writes to.",
                "Per node hour, or per RPU-hour for Redshift Serverless, plus managed storage.",
                "https://docs.aws.amazon.com/redshift/latest/mgmt/welcome.html",
                "https://aws.amazon.com/redshift/pricing/", false),

            new(Tech, "Networking", "amazon-vpc", "Amazon VPC", "Amazon VPC,Amazon Virtual Private Cloud,Internet gateway,NAT gateway,VPC endpoint",
                "A logically isolated network inside AWS where you control subnets, route tables and gateways.",
                "The VPC itself is free; NAT gateways, VPC endpoints and data transfer are charged.",
                "https://docs.aws.amazon.com/vpc/latest/userguide/what-is-amazon-vpc.html",
                "https://aws.amazon.com/vpc/pricing/", true),

            new(Tech, "Networking", "aws-direct-connect", "AWS Direct Connect", "AWS Direct Connect",
                "A dedicated private network connection from your premises to AWS, giving consistent bandwidth and latency that the public internet cannot promise.",
                "Per port hour plus data transfer out; needs physical provisioning lead time.",
                "https://docs.aws.amazon.com/directconnect/latest/UserGuide/Welcome.html",
                "https://aws.amazon.com/directconnect/pricing/", true),

            new(Tech, "Networking", "aws-vpn-and-transit-gateway", "AWS Site-to-Site VPN and AWS Transit Gateway", "AWS Transit Gateway,AWS Site-to-Site VPN,AWS Client VPN",
                "Site-to-Site VPN encrypts a connection to AWS over the public internet; Transit Gateway is the hub that connects many VPCs and on-premises networks together.",
                "VPN per connection hour; Transit Gateway per attachment hour plus data processed.",
                "https://docs.aws.amazon.com/vpn/latest/s2svpn/VPC_VPN.html",
                "https://aws.amazon.com/transit-gateway/pricing/", true),

            new(Tech, "Networking", "amazon-route-53", "Amazon Route 53", "Amazon Route 53",
                "A DNS service that also registers domains, health-checks endpoints, and routes traffic by latency, geography or failover policy.",
                "Per hosted zone per month plus per million queries; domain registration is charged separately.",
                "https://docs.aws.amazon.com/Route53/latest/DeveloperGuide/Welcome.html",
                "https://aws.amazon.com/route53/pricing/", true),

            new(Tech, "Networking", "amazon-cloudfront", "Amazon CloudFront", "Amazon CloudFront",
                "A content delivery network that caches content at edge locations close to users, cutting latency and load on the origin.",
                "Per GB transferred out to the internet plus per request, varying by geographic region.",
                "https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/Introduction.html",
                "https://aws.amazon.com/cloudfront/pricing/", true),

            new(Tech, "Hybrid and edge", "aws-outposts-and-edge", "AWS Outposts, Local Zones, and AWS Wavelength", "AWS Outposts,AWS Local Zones,AWS Wavelength",
                "Ways to run AWS infrastructure outside a Region: Outposts puts AWS racks in your own data centre, Local Zones place compute near large metropolitan areas, Wavelength sits inside 5G networks.",
                "Outposts is a capacity purchase or subscription; Local Zones and Wavelength bill like EC2 at their own rates.",
                "https://docs.aws.amazon.com/outposts/latest/userguide/what-is-outposts.html",
                "https://aws.amazon.com/outposts/pricing/", false),

            new(Tech, "Management", "amazon-cloudwatch", "Amazon CloudWatch", "Amazon CloudWatch",
                "Collects metrics, logs and alarms for AWS resources and applications - performance monitoring, as opposed to CloudTrail's record of who called what.",
                "Per metric, per GB of logs ingested and stored, and per alarm; a free tier covers basic metrics.",
                "https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/WhatIsCloudWatch.html",
                "https://aws.amazon.com/cloudwatch/pricing/", true),

            new(Tech, "Management", "aws-cloudformation", "AWS CloudFormation", "AWS CloudFormation",
                "Provisions infrastructure from a template so an entire environment can be created, updated and deleted repeatably - infrastructure as code.",
                "No additional charge for AWS resource types - you pay for what the template creates.",
                "https://docs.aws.amazon.com/AWSCloudFormation/latest/UserGuide/Welcome.html", null, true),

            new(Tech, "Management", "aws-organizations", "AWS Organizations", "AWS Organizations",
                "Centrally manages multiple AWS accounts, groups them into organizational units, applies service control policies, and consolidates their bills.",
                "No additional charge.",
                "https://docs.aws.amazon.com/organizations/latest/userguide/orgs_introduction.html", null, true),

            new(Tech, "Management", "aws-control-tower", "AWS Control Tower", "AWS Control Tower",
                "Sets up and governs a secure multi-account environment with a landing zone and guardrails, built on top of AWS Organizations.",
                "No additional charge for Control Tower itself; the services it enables are billed.",
                "https://docs.aws.amazon.com/controltower/latest/userguide/what-is-control-tower.html", null, false),

            new(Tech, "Management", "aws-systems-manager", "AWS Systems Manager", "AWS Systems Manager",
                "Operational management of your instances at scale: patching, running commands, Session Manager shell access, and Parameter Store configuration.",
                "Core capabilities are free; advanced parameters and some capabilities are charged.",
                "https://docs.aws.amazon.com/systems-manager/latest/userguide/what-is-systems-manager.html",
                "https://aws.amazon.com/systems-manager/pricing/", false),

            new(Tech, "Application integration", "amazon-sqs-and-sns", "Amazon SQS and Amazon SNS", "Amazon SQS,Amazon SNS,Amazon Simple Queue Service,Amazon Simple Notification Service",
                "SQS is a queue where one consumer pulls each message; SNS is publish/subscribe where one message fans out to many subscribers. The pair the exam uses for decoupling.",
                "Per million requests for both, with a generous free tier.",
                "https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html",
                "https://aws.amazon.com/sqs/pricing/", true),

            new(Tech, "Analytics", "analytics-services", "Amazon Athena, AWS Glue, and Amazon QuickSight", "Amazon Athena,AWS Glue,Amazon QuickSight",
                "Athena queries data in S3 with SQL and no servers; Glue is the managed ETL service and data catalog; QuickSight is the business intelligence dashboard on top.",
                "Athena per TB scanned, Glue per DPU-hour, QuickSight per user per month.",
                "https://docs.aws.amazon.com/athena/latest/ug/what-is.html",
                "https://aws.amazon.com/athena/pricing/", true),

            new(Tech, "Machine learning", "aws-ai-services", "The AWS AI and ML services", "Amazon SageMaker,Amazon SageMaker AI,Amazon Bedrock,Amazon Rekognition,Amazon Comprehend,Amazon Polly,Amazon Lex,Amazon Translate,Amazon Transcribe,Amazon Kendra,Amazon Personalize",
                "Pre-trained services that need no ML expertise - Rekognition for images, Comprehend for text, Polly for speech synthesis, Transcribe for speech to text, Translate for language, Lex for chatbots - with Amazon SageMaker AI for building models and Amazon Bedrock for calling foundation models.",
                "Priced per unit processed (image, character, minute) or per token.",
                "https://docs.aws.amazon.com/whitepapers/latest/aws-overview/machine-learning.html", null, false),

            new(Tech, "End-user computing", "amazon-workspaces", "Amazon WorkSpaces", "Amazon WorkSpaces",
                "Managed virtual desktops in the cloud, delivered to any supported device.",
                "Per desktop per month, or hourly plus a small monthly fee per desktop.",
                "https://docs.aws.amazon.com/workspaces/latest/adminguide/amazon-workspaces.html",
                "https://aws.amazon.com/workspaces-family/workspaces/pricing/", false),

            new(Tech, "Access", "ways-to-access-aws", "The Management Console, AWS CLI, SDKs, and CloudShell", "AWS CLI,AWS SDK,AWS Management Console,AWS CloudShell",
                "The four ways to interact with AWS: the browser console, the command line interface, the language SDKs, and CloudShell - a browser shell with the CLI already authenticated.",
                "All free to use; CloudShell includes persistent storage at no charge.",
                "https://docs.aws.amazon.com/cli/latest/userguide/cli-chap-welcome.html", null, true),

            // ---------- Domain 4: Billing, Pricing, and Support ----------
            new(Billing, "Pricing models", "aws-pricing-fundamentals", "How AWS pricing works", "AWS Billing",
                "Pay-as-you-go, save when you commit, and pay less as you use more. Compute, storage and data transfer out are the three things that drive almost every bill.",
                "Data transfer IN is generally free; data transfer OUT to the internet is what surprises people.",
                "https://docs.aws.amazon.com/whitepapers/latest/how-aws-pricing-works/welcome.html", null, true),

            new(Billing, "Pricing models", "ec2-purchasing-options", "EC2 purchasing options", "Reserved Instances,On-Demand Instances,Dedicated Hosts,Spot Instances",
                "On-Demand for unpredictable short workloads, Reserved Instances and Savings Plans for steady 1- or 3-year usage, Spot for interruptible fault-tolerant work at a deep discount, Dedicated Hosts for licensing or compliance requirements.",
                "The exam tests which option a described workload should use, and Spot's interruption trade-off.",
                "https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/instance-purchasing-options.html",
                "https://aws.amazon.com/ec2/pricing/", true),

            new(Billing, "Pricing models", "savings-plans", "Savings Plans", "Savings Plans",
                "A commitment to a consistent hourly spend for 1 or 3 years in exchange for lower rates, more flexible across instance families and Regions than Reserved Instances.",
                "Compute Savings Plans also cover Lambda and Fargate; EC2 Instance Savings Plans discount more but lock you to a family in a Region.",
                "https://docs.aws.amazon.com/savingsplans/latest/userguide/what-is-savings-plans.html",
                "https://aws.amazon.com/savingsplans/pricing/", true),

            new(Billing, "Pricing models", "aws-free-tier", "AWS Free Tier", "AWS Free Tier",
                "The exam tests three kinds of free offer: always free (such as Lambda's monthly requests), 12 months free for new accounts, and short-term trials. NOTE, AWS has since restructured the Free Tier around a Free plan (credits over 6 months), a Paid plan, short-term trials and 30+ always-free services. Answer exam questions with the three-type model.",
                "Exceeding a free tier limit moves you onto standard rates without warning - set a budget.",
                "https://docs.aws.amazon.com/awsaccountbilling/latest/aboutv2/billing-free-tier.html",
                "https://aws.amazon.com/free/", true),

            new(Billing, "Cost management", "aws-pricing-calculator", "AWS Pricing Calculator", "AWS Pricing Calculator",
                "Estimates the cost of an architecture BEFORE you build it - the planning tool, as opposed to Cost Explorer which reports what you have already spent.",
                "Free to use, and no AWS account is required.",
                "https://docs.aws.amazon.com/pricing-calculator/latest/userguide/what-is-pricing-calculator.html", null, true),

            new(Billing, "Cost management", "aws-cost-explorer", "AWS Cost Explorer", "AWS Cost Explorer",
                "Visualises and forecasts past and current spend, filtered and grouped by service, account or tag.",
                "The console interface is free; the Cost Explorer API is charged per request.",
                "https://docs.aws.amazon.com/cost-management/latest/userguide/ce-what-is.html", null, true),

            new(Billing, "Cost management", "aws-budgets", "AWS Budgets", "AWS Budgets",
                "Sets cost or usage thresholds and alerts you - or triggers an action - when spend is forecast to cross them.",
                "Your first two action-enabled budgets are free; each additional action-enabled budget costs $0.10 per day.",
                "https://docs.aws.amazon.com/cost-management/latest/userguide/budgets-managing-costs.html", null, true),

            new(Billing, "Cost management", "cost-and-usage-report", "AWS Cost and Usage Report", "AWS Cost and Usage Report",
                "The most detailed billing data AWS publishes, delivered to Amazon S3 for analysis - line items down to the hour and the individual resource.",
                "The report itself is free; you pay for the S3 storage it is delivered to.",
                "https://docs.aws.amazon.com/cur/latest/userguide/what-is-cur.html", null, false),

            new(Billing, "Cost management", "consolidated-billing", "Consolidated billing and cost allocation tags", null,
                "One bill for every account in an organization, with volume discounts pooled across them, and tags that attribute cost to a team, project or environment.",
                "No additional charge; the saving comes from pooled tiered usage and shared reservations.",
                "https://docs.aws.amazon.com/awsaccountbilling/latest/aboutv2/consolidated-billing.html", null, true),

            new(Billing, "Optimisation", "aws-trusted-advisor", "AWS Trusted Advisor", "AWS Trusted Advisor",
                "Inspects your account and recommends fixes. The exam tests five categories: cost optimization, performance, security, fault tolerance and service limits.",
                "Basic Support gets all Service Limits checks plus selected Security and Fault tolerance checks; the full set needs a paid plan (the exam says Business or Enterprise; AWS now names Business Support+, Enterprise Support or Unified Operations).",
                "https://docs.aws.amazon.com/awssupport/latest/user/trusted-advisor.html", null, true),

            new(Billing, "Optimisation", "aws-compute-optimizer", "AWS Compute Optimizer", "AWS Compute Optimizer",
                "Analyses utilisation metrics and recommends right-sizing for EC2, EBS, Lambda and ECS on Fargate.",
                "No additional charge for the standard recommendations.",
                "https://docs.aws.amazon.com/compute-optimizer/latest/ug/what-is-compute-optimizer.html", null, false),

            new(Billing, "Support", "aws-support-plans", "AWS Support plans", "AWS Support,AWS Knowledge Center,AWS re:Post",
                "The exam tests the five-plan model: Basic, Developer, Business, Enterprise On-Ramp and Enterprise - differing by response time, who you may contact, and whether you get a Technical Account Manager (Enterprise On-Ramp and Enterprise do). NOTE, AWS has since restructured: the current plans are Basic, Business Support+, Enterprise Support and AWS Unified Operations, with Developer, Business and Enterprise On-Ramp ending 1 January 2027. Answer exam questions with the five-plan model.",
                "Basic is free. Paid plans cost a percentage of monthly AWS spend, subject to a minimum.",
                "https://docs.aws.amazon.com/awssupport/latest/user/aws-support-plans.html",
                "https://aws.amazon.com/premiumsupport/pricing/", true),

            new(Billing, "Support", "aws-marketplace", "AWS Marketplace", "AWS Marketplace",
                "A catalogue of third-party software you can buy and deploy into your account, billed through your existing AWS bill.",
                "Charges appear on your AWS invoice; some listings count toward committed spend agreements.",
                "https://docs.aws.amazon.com/marketplace/latest/buyerguide/what-is-marketplace.html", null, false),

            new(Billing, "Support", "aws-health-dashboard", "AWS Health Dashboard", "AWS Health Dashboard",
                "Shows the health of AWS services generally and, more usefully, the events that affect your own resources and accounts.",
                "No charge.",
                "https://docs.aws.amazon.com/health/latest/ug/what-is-aws-health.html", null, false),

            new(Billing, "Support", "aws-professional-services-and-partners", "AWS Professional Services and the AWS Partner Network", "AWS Professional Services",
                "AWS consultants and the network of third-party partners who help design and migrate workloads - the answer when a question asks who helps beyond a support plan.",
                "Engagement-based pricing, quoted per project.",
                "https://aws.amazon.com/professional-services/", null, false),
        ],
    };
}
