using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using AwsCertPrep.Api.Application.Abstractions;
using AwsCertPrep.Api.Application.Options;
using Microsoft.Extensions.Options;

namespace AwsCertPrep.Api.Infrastructure.Messaging;

/// <summary>
/// Resolves a request's handler from the container and runs it through the registered pipeline.
///
/// Hand-written rather than taken from a package: the mediator pattern here is about sixty lines,
/// and the well-known library moved to a commercial licence. This keeps the app dependency-free
/// and the behaviour visible.
///
/// The reflection needed to bridge from <c>IRequest&lt;TResponse&gt;</c> to the closed handler type
/// is done once per request type and cached, so dispatch is a dictionary lookup and a delegate
/// call rather than reflection on every call.
/// </summary>
public class Mediator(IServiceProvider services) : IMediator
{
    private static readonly ConcurrentDictionary<Type, Type> WrapperTypes = new();

    public Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wrapperType = WrapperTypes.GetOrAdd(
            request.GetType(),
            requestType => typeof(RequestWrapper<,>).MakeGenericType(requestType, typeof(TResponse)));

        var wrapper = (RequestWrapperBase<TResponse>)Activator.CreateInstance(wrapperType)!;
        return wrapper.HandleAsync(request, services, ct);
    }

    private abstract class RequestWrapperBase<TResponse>
    {
        public abstract Task<TResponse> HandleAsync(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class RequestWrapper<TRequest, TResponse> : RequestWrapperBase<TResponse>
        where TRequest : IRequest<TResponse>
    {
        public override Task<TResponse> HandleAsync(
            IRequest<TResponse> request, IServiceProvider services, CancellationToken ct)
        {
            var handler = services.GetService<IRequestHandler<TRequest, TResponse>>()
                          ?? throw new InvalidOperationException(
                              $"No handler registered for {typeof(TRequest).Name}.");

            var typed = (TRequest)request;

            RequestHandlerDelegate<TResponse> next = () => handler.HandleAsync(typed, ct);

            // Reversed so the first-registered behaviour ends up outermost.
            var behaviours = services
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .Reverse();

            foreach (var behaviour in behaviours)
            {
                var inner = next;
                next = () => behaviour.HandleAsync(typed, inner, ct);
            }

            return next();
        }
    }
}

/// <summary>
/// Validates a request's data annotations before it reaches its handler.
///
/// Controllers already validate a bound body, but a request can also be built in code - from
/// another handler, or a background job - and those paths would otherwise skip validation
/// entirely. Doing it here means the rule holds wherever the request came from.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var context = new ValidationContext(request!);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(request!, context, results, validateAllProperties: true))
        {
            var message = string.Join(" ", results.Select(r => r.ErrorMessage));
            logger.LogInformation("Rejected {Request}: {Message}", typeof(TRequest).Name, message);
            throw new ValidationException(message);
        }

        return next();
    }
}

/// <summary>
/// Logs what each request did and how long it took, and marks the slow ones.
///
/// Useful precisely because generation and tutor requests call a provider: a handler taking
/// thirty seconds is normal for those and alarming for anything else, and this is the one place
/// that can tell the difference without instrumenting every handler.
/// </summary>
public class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    IOptions<PipelineOptions> options)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly PipelineOptions _options = options.Value;

    public async Task<TResponse> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var kind = request is ICommand<TResponse> ? "command" : "query";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await next();
            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds > _options.SlowRequestMilliseconds)
                logger.LogInformation(
                    "{Kind} {Name} completed in {Elapsed}ms.", kind, name, stopwatch.ElapsedMilliseconds);
            else
                logger.LogDebug(
                    "{Kind} {Name} completed in {Elapsed}ms.", kind, name, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex, "{Kind} {Name} failed after {Elapsed}ms.", kind, name, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}

public static class MediatorRegistration
{
    /// <summary>
    /// Registers the mediator, every handler in this assembly, and the pipeline behaviours.
    ///
    /// Handlers are discovered by scanning rather than listed by hand: a list is a second place to
    /// remember, and forgetting an entry fails at runtime on one endpoint rather than at build.
    /// </summary>
    public static IServiceCollection AddMediator(this IServiceCollection services)
    {
        services.AddScoped<IMediator, Mediator>();

        var handlerInterface = typeof(IRequestHandler<,>);

        var handlers =
            from type in Assembly.GetExecutingAssembly().GetTypes()
            where type is { IsAbstract: false, IsInterface: false }
            from @interface in type.GetInterfaces()
            where @interface.IsGenericType && @interface.GetGenericTypeDefinition() == handlerInterface
            select new { Implementation = type, Service = @interface };

        foreach (var handler in handlers)
            services.AddScoped(handler.Service, handler.Implementation);

        // Order matters: validation runs before logging records a duration for work never done.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

        return services;
    }
}
