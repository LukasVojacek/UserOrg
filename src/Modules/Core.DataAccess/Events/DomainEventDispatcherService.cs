using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.DataAccess.Events;

/// <summary>
/// Background service that reads domain events from DomainEventChannel and dispatches them
/// to all registered IDomainEventHandler implementations.
/// Internal implementation — infrastructure detail of Core.DataAccess.
///
/// External propagation (webhooks, message broker) is prepared:
/// events that also implement IExternalEvent are marked for future routing.
/// </summary>
internal sealed class DomainEventDispatcherService(
    DomainEventChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<DomainEventDispatcherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Domain event dispatcher started.");

        await foreach (var domainEvent in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(domainEvent, stoppingToken);
        }

        logger.LogInformation("Domain event dispatcher stopped.");
    }

    private async Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType();

        try
        {
            // Create a scope per event so handlers can use scoped services (e.g., DbContext).
            await using var scope = scopeFactory.CreateAsyncScope();

            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handlers = scope.ServiceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                if (handler is IDomainEventHandler h)
                    await h.HandleAsync(domainEvent, cancellationToken);
            }

            // Prepared for future external propagation.
            if (domainEvent is IExternalEvent)
            {
                // TODO: route to external bus / webhook dispatcher.
                logger.LogDebug("External event {EventType} is ready for external propagation (not yet implemented).",
                    eventType.Name);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching domain event {EventType}.", eventType.Name);
        }
    }
}