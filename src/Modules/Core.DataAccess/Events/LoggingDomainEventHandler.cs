using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Events;

/// <summary>
/// Default domain event handler — logs every event.
/// Internal implementation — infrastructure detail of Core.DataAccess.
/// Registered for all IDomainEvent types so there is always at least one handler.
/// </summary>
internal sealed class LoggingDomainEventHandler<TEvent>(ILogger<LoggingDomainEventHandler<TEvent>> logger)
    : IDomainEventHandler<TEvent> where TEvent : IDomainEvent
{
    public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Domain event raised: {EventType} — {Event}",
            typeof(TEvent).Name,
            domainEvent);

        return Task.CompletedTask;
    }
}