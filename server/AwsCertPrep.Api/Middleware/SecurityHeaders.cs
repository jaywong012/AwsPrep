namespace AwsCertPrep.Api.Middleware;

/// <summary>
/// Response headers every API response should carry. This service only ever returns JSON to a
/// separately hosted SPA, so the policy can be strict: nothing is framed, nothing is sniffed,
/// no referrer leaks, and no browser feature is granted.
/// </summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-Frame-Options"] = "DENY";
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

            // A JSON API renders nothing itself. Blocking every source keeps a response that
            // somehow gets rendered directly (a browser opening an error page, say) inert.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            await next();
        });
}
