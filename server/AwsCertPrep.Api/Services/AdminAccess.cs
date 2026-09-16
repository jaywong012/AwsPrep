using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Services;

public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Shared secret for the destructive endpoints (deleting a question, applying an audit
    /// cleanup). Store it in user-secrets, an environment variable or a key vault - never in
    /// appsettings.json. Leave it unset outside Development to switch those endpoints off.
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Whether a request carries the configured administrator key.
///
/// Deliberately false when no key is configured. An unset key must never widen access, so the
/// operator bypasses this grants - skipping the learner rate limits, for instance - stay switched
/// off until someone deliberately configures Admin:ApiKey. That is the opposite of
/// <see cref="AdminOnlyFilter"/>, which stays open in Development so the local UI keeps working:
/// letting a page through is safe, handing out an unlimited generation budget is not.
/// </summary>
public static class AdminKey
{
    public static bool IsVerifiedOperator(this HttpContext context)
    {
        var configured = context.RequestServices.GetService<IOptions<AdminOptions>>()?.Value.ApiKey;

        return !string.IsNullOrWhiteSpace(configured)
               && Matches(context.Request.Headers[AdminOnlyFilter.HeaderName].ToString(), configured);
    }

    /// <summary>
    /// Compares hashes rather than the strings themselves: FixedTimeEquals needs equal-length
    /// inputs, and hashing both sides keeps the comparison free of a length side channel.
    /// </summary>
    internal static bool Matches(string presented, string configured)
    {
        if (presented.Length == 0) return false;

        var a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}

/// <summary>
/// Guards the endpoints that destroy shared data. The app has no user accounts, so every
/// endpoint is reachable by anyone who can reach the API; that is tolerable for reads and for
/// a learner's own sessions, but not for deleting questions out of a bank everyone shares.
///
/// Behaviour: with Admin:ApiKey configured, callers must present it in X-Admin-Key. Without it,
/// the endpoints stay open in Development (so the local UI keeps working) and are refused
/// everywhere else, which fails closed rather than silently unguarded in production.
/// </summary>
public class AdminOnlyFilter(
    IOptions<AdminOptions> options,
    IHostEnvironment environment,
    ILogger<AdminOnlyFilter> logger) : IAuthorizationFilter
{
    public const string HeaderName = "X-Admin-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configured = options.Value.ApiKey;

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsDevelopment()) return;

            context.Result = Problem(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Administration disabled",
                $"This endpoint changes shared data and is disabled until {AdminOptions.SectionName}:ApiKey is configured.");
            return;
        }

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (!AdminKey.Matches(presented, configured))
        {
            logger.LogWarning(
                "Rejected an administrative call to {Path} with {Outcome} admin key.",
                context.HttpContext.Request.Path,
                presented.Length == 0 ? "no" : "an incorrect");

            context.Result = Problem(
                context,
                StatusCodes.Status403Forbidden,
                "Administrator key required",
                $"Send the administrator key in the {HeaderName} header to use this endpoint.");
        }
    }

    private static ObjectResult Problem(
        AuthorizationFilterContext context, int status, string title, string detail) =>
        new(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.HttpContext.Request.Path,
        })
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
}
