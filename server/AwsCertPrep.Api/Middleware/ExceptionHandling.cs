using System.Diagnostics;
using AwsCertPrep.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace AwsCertPrep.Api.Middleware;

/// <summary>Maps domain exceptions to clean ProblemDetails responses the SPA can display.</summary>
public static class ExceptionHandling
{
    public static IApplicationBuilder UseExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var ex = feature?.Error;

            var (status, title) = ex switch
            {
                KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid request"),
                AiConfigurationException => (StatusCodes.Status503ServiceUnavailable, "AI provider not configured"),
                AiProviderException => (StatusCodes.Status502BadGateway, "AI provider error"),
                TaskCanceledException or TimeoutException => (StatusCodes.Status504GatewayTimeout, "AI provider timed out"),
                _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
            };

            // A trace id in the response is what makes a user's "it failed" report findable in
            // the logs, so it is attached to every problem and logged alongside the exception.
            var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

            if (status == StatusCodes.Status500InternalServerError)
            {
                app.Logger.LogError(ex, "Unhandled exception on {Path} ({TraceId})", context.Request.Path, traceId);
            }
            else
            {
                app.Logger.LogInformation(
                    "Request failed on {Path} with {Status}: {Message} ({TraceId})",
                    context.Request.Path, status, ex?.Message, traceId);
            }

            context.Response.StatusCode = status;

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = status == StatusCodes.Status500InternalServerError
                    // An unexpected failure's message can carry internals (connection strings,
                    // stack context), so only the trace id crosses the wire.
                    ? "Something went wrong. Quote the trace id when reporting it."
                    : ex?.Message,
                Instance = context.Request.Path,
            };
            problem.Extensions["traceId"] = traceId;

            // The overload that takes a content type: WriteAsJsonAsync(value) would overwrite
            // Response.ContentType with application/json on its way out.
            await context.Response.WriteAsJsonAsync(
                problem, options: null, contentType: "application/problem+json");
        }));

        return app;
    }
}
