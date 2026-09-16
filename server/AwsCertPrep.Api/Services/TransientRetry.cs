using System.Net;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Free-tier LLM endpoints return 429/503 under load fairly often, so a couple of
/// backed-off retries turn most of those into a successful generation.
/// </summary>
public static class TransientRetry
{
    private static readonly HttpStatusCode[] Transient =
    [
        HttpStatusCode.TooManyRequests,      // 429 rate limit
        HttpStatusCode.InternalServerError,  // 500
        HttpStatusCode.BadGateway,           // 502
        HttpStatusCode.ServiceUnavailable,   // 503 model overloaded
        HttpStatusCode.GatewayTimeout        // 504
    ];

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(6)
    ];

    /// <param name="send">Creates and sends a fresh request. Must build a new
    /// HttpRequestMessage each call - a sent message cannot be reused.</param>
    public static async Task<(HttpResponseMessage Response, string Body)> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        ILogger logger,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await send(ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode || !Transient.Contains(response.StatusCode) || attempt >= Delays.Length)
                return (response, body);

            var delay = response.Headers.RetryAfter?.Delta ?? Delays[attempt];
            logger.LogInformation(
                "Provider returned {Status}; retrying in {Delay}s (attempt {Attempt} of {Max}).",
                (int)response.StatusCode, delay.TotalSeconds, attempt + 1, Delays.Length);

            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }
}
