using System.Threading.Channels;

namespace Core.DataAccess.Events;

/// <summary>
/// Singleton channel used to pass domain events from IUnitOfWork to the background dispatcher.
/// Internal implementation — infrastructure detail of Core.DataAccess.
/// Unbounded — producers never block; dispatcher processes events asynchronously.
/// </summary>
internal sealed class DomainEventChannel
{
    private readonly Channel<IDomainEvent> _channel = Channel.CreateUnbounded<IDomainEvent>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    public ChannelWriter<IDomainEvent> Writer => _channel.Writer;
    public ChannelReader<IDomainEvent> Reader => _channel.Reader;
}