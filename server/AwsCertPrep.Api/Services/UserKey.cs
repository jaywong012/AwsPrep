using System.Text.RegularExpressions;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Reads the learner identity from the request. There is no authentication yet: the SPA
/// generates a stable key per browser and sends it in X-User-Key, and everything owned by a
/// learner (exam sessions, history, readiness) is scoped to that value.
///
/// The value reaches the database and the rate limiter, so it is sanitised rather than trusted:
/// anything outside a conservative character set is rejected and treated as anonymous.
/// </summary>
public static class UserKeyAccessor
{
    public const string HeaderName = "X-User-Key";

    /// <summary>Used when the caller sends no usable key, so the API still works without the SPA.</summary>
    public const string Anonymous = "local";

    private const int MaxLength = 100;

    private static readonly Regex Allowed = new(@"^[A-Za-z0-9._:@-]{1,100}$", RegexOptions.Compiled);

    public static string GetUserKey(this HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values)) return Anonymous;

        var candidate = values.ToString().Trim();
        if (candidate.Length == 0 || candidate.Length > MaxLength) return Anonymous;

        return Allowed.IsMatch(candidate) ? candidate : Anonymous;
    }
}
