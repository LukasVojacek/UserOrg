namespace UserOrg.Shared.Abstractions.Interfaces;

/// <summary>
/// Defines a handler for processing a request and producing a response asynchronously.
/// </summary>
/// <remarks>Implement this interface to define the logic for handling a specific request type. Handlers are
/// typically used in request/response messaging patterns to decouple request processing from request
/// dispatching.</remarks>
/// <typeparam name="TRequest">The type of request to handle. Must implement <see cref="IRequest{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Handles the specified request asynchronously and returns a response of the specified type.
    /// </summary>
    /// <param name="request">The request message to process. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response to the request.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}