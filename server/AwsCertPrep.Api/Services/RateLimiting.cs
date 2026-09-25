using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace AwsCertPrep.Api.Services;

public class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Set false only where an upstream gateway already enforces limits.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Generation calls per learner per hour. Each one hits the LLM provider's quota.</summary>
    public int GeneratePerHour { get; set; } = 30;

    /// <summary>
    /// Generation calls per client address per hour. Registration is open, so a determined caller
    /// can mint accounts to reset the per-learner bucket; this is the limit that survives that
    /// and actually caps what one source can spend of the provider's quota.
    /// </summary>
    public int GeneratePerHourPerAddress { get; set; } = 60;

    /// <summary>Readiness calls per learner per minute. Each one trains an ML.NET model.</summary>
    public int InsightsPerMinute { get; set; } = 20;

    /// <summary>Catch-all per client address per minute, so one caller cannot saturate the API.</summary>
    public int GlobalPerMinute { get; set; } = 300;

    /// <summary>
    /// Sign-in and registration attempts per client address per fifteen minutes. This is the
    /// per-source half of the brute-force defence; Identity's account lockout is the per-account
    /// half. Neither covers the other's case - lockout does nothing against someone spraying one
    /// password across many addresses, and an address limit does nothing against a botnet
    /// grinding one account.
    /// </summary>
    public int AuthPerFifteenMinutes { get; set; } = 10;
}

/// <summary>
/// Rate limits for the endpoints where a single caller can cost real money (LLM generation) or
/// real CPU (ML.NET training), plus a global ceiling per client address.
///
/// Partitioning is by authenticated account where there is one and by remote address otherwise.
/// Since the subject is now signed rather than self-asserted, the per-learner limit is a real
/// control on a real identity - but registration is open, so the per-address ceilings remain the
/// backstop against someone making new accounts to get a fresh bucket.
/// </summary>
public static class RateLimitPolicies
{
    public const string Generate = "generate";
    public const string Insights = "insights";
    public const string Auth = "auth";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                      ?? new RateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, ct) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var window)
                    ? (int)Math.Ceiling(window.TotalSeconds)
                    : 60;

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfter.ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.ContentType = "application/problem+json";

                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    status = StatusCodes.Status429TooManyRequests,
                    title = "Too many requests",
                    detail = $"Rate limit reached. Try again in about {retryAfter} seconds.",
                }), ct);
            };

            if (!options.Enabled) return;

            // A verified operator is exempt. The limits exist to stop one learner spending the
            // provider budget, but bulk authoring - filling the bank, writing every lesson - is a
            // legitimate administrative job that would otherwise need the limits edited by hand.
            limiter.AddPolicy(Generate, context => context.IsVerifiedOperator()
                ? RateLimitPartition.GetNoLimiter(OperatorPartition)
                : FixedWindow(PartitionKey(context), options.GeneratePerHour, TimeSpan.FromHours(1)));

            limiter.AddPolicy(Insights, context => context.IsVerifiedOperator()
                ? RateLimitPartition.GetNoLimiter(OperatorPartition)
                : FixedWindow(PartitionKey(context), options.InsightsPerMinute, TimeSpan.FromMinutes(1)));

            // Sign-in and registration. Always per address - the caller is unauthenticated by
            // definition, and partitioning on the submitted email would hand an attacker a fresh
            // bucket per guess. No operator bypass either: there is no legitimate bulk login.
            limiter.AddPolicy(Auth, context =>
                FixedWindow($"auth:{Address(context)}", options.AuthPerFifteenMinutes, TimeSpan.FromMinutes(15)));

            // Chained: every request counts against the per-address ceiling, and a generation
            // request additionally counts against the per-address generation budget. Registering
            // a second account sidesteps the named policy above but not these.
            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.IsVerifiedOperator()
                        ? RateLimitPartition.GetNoLimiter(OperatorPartition)
                        : FixedWindow($"all:{Address(context)}", options.GlobalPerMinute, TimeSpan.FromMinutes(1))),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsGenerationRequest(context) && !context.IsVerifiedOperator()
                        ? FixedWindow($"generate:{Address(context)}", options.GeneratePerHourPerAddress, TimeSpan.FromHours(1))
                        : RateLimitPartition.GetNoLimiter("exempt")));
        });

        return services;
    }

    /// <summary>Shared no-limiter partition for verified operators.</summary>
    private const string OperatorPartition = "operator";

    private static RateLimitPartition<string> FixedWindow(string key, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit),
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    /// <summary>
    /// Requests that cost a provider call, and so count against the per-address generation
    /// budget: generating questions, and writing or rewriting a lesson's notes.
    /// </summary>
    private static bool IsGenerationRequest(HttpContext context)
    {
        var path = context.Request.Path;

        if (HttpMethods.IsPost(context.Request.Method)
            && path.StartsWithSegments("/api/questions/generate", StringComparison.OrdinalIgnoreCase))
            return true;

        return (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
               && path.StartsWithSegments("/api/lessons", StringComparison.OrdinalIgnoreCase)
               && path.Value?.EndsWith("/notes", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string Address(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// The bucket a request counts against: the signed-in account, or the client address when
    /// there is no principal.
    ///
    /// The address fallback is deliberate rather than a throw. Every endpoint carrying a named
    /// policy also carries [Authorize], so in practice this only ever returns a user partition -
    /// but if the pipeline were ever reordered so authentication ran after the limiter, degrading
    /// to a stricter shared bucket is a better failure than an exception on every request.
    /// </summary>
    private static string PartitionKey(HttpContext context)
    {
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? $"ip:{Address(context)}" : $"user:{userId}";
    }
}
