using System.Net;

namespace AwsCertPrep.Api.Services;

/// <summary>
/// Turns a provider HTTP failure into a message worth showing a user.
///
/// The provider's own error body is logged, never returned: it is upstream internals, it says
/// nothing the user can act on, and it is the kind of payload that quietly grows to include
/// request echoes. What a user needs is which knob to turn, so that is what these say.
/// </summary>
public static class AiProviderErrors
{
    public static string Describe(string provider, HttpStatusCode status, string? model = null)
    {
        var code = (int)status;

        return status switch
        {
            HttpStatusCode.TooManyRequests =>
                $"{provider} is rate limiting this API key ({code}). Wait a minute, or ask for fewer questions per call.",

            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                $"{provider} rejected the API key ({code}). Check the Ai:ApiKey value.",

            HttpStatusCode.NotFound =>
                $"{provider} has no model named '{model}' for this key ({code}). Check the Ai:Model value.",

            HttpStatusCode.BadRequest =>
                $"{provider} rejected the request ({code}). Check that Ai:Model is a model this key can use.",

            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                $"{provider} is temporarily unavailable ({code}). Free tiers shed load under demand; try again shortly.",

            _ => $"{provider} returned {code}. See the API logs for the provider's response.",
        };
    }
}
