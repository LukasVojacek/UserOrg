using Lukdrasil.Error;
using Lukdrasil.Result;

using Waldur.Contracts.Events;

namespace Waldur.Contracts;

/// <summary>
/// The publish seam between the Waldur pull side and the consumer (the future RabbitMQ boundary).
/// The in-process implementation completes only once the corresponding handler completed, so a
/// returned task = ack (ADR-0056: the SHA map is updated only after this awaits successfully).
/// </summary>
public interface IWaldurEventPublisher
{
    /// <summary>
    /// Publishes a single fat event. A <see cref="Result{TError}"/> failure (the handler's expected
    /// dependency-missing outcome) means "not acked" — retried next iteration — as does a thrown
    /// exception (an unexpected fault caught by the worker's backstop). Success = acked (ADR-0065).
    /// </summary>
    Task<Result<Error>> PublishAsync(WaldurIntegrationEvent evt, CancellationToken ct);
}