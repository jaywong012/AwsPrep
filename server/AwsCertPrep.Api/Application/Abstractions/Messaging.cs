namespace AwsCertPrep.Api.Application.Abstractions;

/// <summary>
/// A request that produces <typeparamref name="TResponse"/>. Handled by exactly one
/// <see cref="IRequestHandler{TRequest,TResponse}"/>, resolved through the container.
/// </summary>
public interface IRequest<out TResponse>;

/// <summary>
/// A request that changes state. Split from <see cref="IQuery{T}"/> so the two sides are
/// distinguishable at the type level: a pipeline behaviour, a controller, or a reader can tell a
/// write from a read without reading the body, which is the point of separating them at all.
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>A request that only reads. Handlers for these never call SaveChanges.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;

/// <summary>Handles exactly one request type.</summary>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken ct);
}

/// <summary>Convenience alias so a command handler reads as one.</summary>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

/// <summary>Convenience alias so a query handler reads as one.</summary>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;

/// <summary>The next step in the pipeline.</summary>
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>
/// Wraps handling of every request. Behaviours run in registration order, outermost first, so
/// cross-cutting concerns - validation, logging, timing - live in one place instead of being
/// repeated at the top of every handler.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
}

/// <summary>
/// Sends a request to its handler.
///
/// The point of routing through this rather than injecting services into controllers is that a
/// controller ends up depending on one abstraction instead of a growing list of them, and every
/// request passes the same pipeline whatever called it.
/// </summary>
public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}
