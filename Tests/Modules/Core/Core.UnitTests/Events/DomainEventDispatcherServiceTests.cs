using System.Collections.Concurrent;

using Core.DataAccess.Events;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Events;

/// <summary>
/// Characterization tests for <see cref="DomainEventDispatcherService"/>: the loop reads the channel until the
/// writer completes, dispatches each event to every handler of its type from a scope of its own, logs a failing
/// dispatch without stopping, and marks an <see cref="IExternalEvent"/> as ready for external propagation.
/// </summary>
public sealed class DomainEventDispatcherServiceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Each_event_reaches_every_handler_of_its_type_from_a_scope_of_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new HandlerLog();
        await using var provider = BuildProvider(log, services =>
        {
            services.AddScoped<IDomainEventHandler<SampleEvent>, FirstSampleHandler>();
            services.AddScoped<IDomainEventHandler<SampleEvent>, SecondSampleHandler>();
        });
        var logger = new CapturingLogger<DomainEventDispatcherService>();

        await RunToCompletionAsync(provider, logger, ct, new SampleEvent("a"), new SampleEvent("b"));

        Assert.Equal(
            ["first:a", "second:a", "first:b", "second:b"],
            log.Calls.Select(c => $"{c.Handler}:{c.Payload}"));
        Assert.Equal(2, log.Calls.Select(c => c.Scope).Distinct().Count());
        Assert.Single(log.Calls.Where(c => c.Payload == "a").Select(c => c.Scope).Distinct());
    }

    [Fact]
    public async Task A_failing_handler_is_logged_and_the_next_event_is_still_dispatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new HandlerLog();
        await using var provider = BuildProvider(log, services =>
            services.AddScoped<IDomainEventHandler<SampleEvent>, FirstSampleHandler>());
        var logger = new CapturingLogger<DomainEventDispatcherService>();

        await RunToCompletionAsync(provider, logger, ct, new SampleEvent(HandlerLog.Throw), new SampleEvent("after"));

        Assert.Equal(["after"], log.Calls.Select(c => c.Payload));
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal($"Error dispatching domain event {nameof(SampleEvent)}.", error.Message);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task An_event_without_a_handler_is_skipped_without_an_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new HandlerLog();
        await using var provider = BuildProvider(log, services =>
            services.AddScoped<IDomainEventHandler<SampleEvent>, FirstSampleHandler>());
        var logger = new CapturingLogger<DomainEventDispatcherService>();

        await RunToCompletionAsync(provider, logger, ct, new UnhandledEvent(), new SampleEvent("handled"));

        Assert.Equal(["handled"], log.Calls.Select(c => c.Payload));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task An_external_event_is_handled_and_marked_ready_for_external_propagation()
    {
        var ct = TestContext.Current.CancellationToken;
        var log = new HandlerLog();
        await using var provider = BuildProvider(log, services =>
            services.AddScoped<IDomainEventHandler<SampleExternalEvent>, ExternalHandler>());
        var logger = new CapturingLogger<DomainEventDispatcherService>();

        await RunToCompletionAsync(provider, logger, ct, new SampleEvent("internal"), new SampleExternalEvent("outbound"));

        Assert.Equal(["outbound"], log.Calls.Select(c => c.Payload));
        var debug = Assert.Single(logger.Entries, e => e.Level == LogLevel.Debug);
        Assert.Equal(
            $"External event {nameof(SampleExternalEvent)} is ready for external propagation (not yet implemented).",
            debug.Message);
    }

    [Fact]
    public async Task The_loop_logs_start_and_stop_around_a_completed_channel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var provider = BuildProvider(new HandlerLog(), _ => { });
        var logger = new CapturingLogger<DomainEventDispatcherService>();

        await RunToCompletionAsync(provider, logger, ct);

        Assert.Equal(
            ["Domain event dispatcher started.", "Domain event dispatcher stopped."],
            logger.Entries.Where(e => e.Level == LogLevel.Information).Select(e => e.Message));
    }

    private static ServiceProvider BuildProvider(HandlerLog log, Action<IServiceCollection> handlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddScoped<ScopeMarker>();
        handlers(services);
        return services.BuildServiceProvider();
    }

    private static async Task RunToCompletionAsync(
        ServiceProvider provider,
        CapturingLogger<DomainEventDispatcherService> logger,
        CancellationToken ct,
        params IDomainEvent[] events)
    {
        var channel = new DomainEventChannel();
        foreach (var domainEvent in events)
        {
            await channel.Writer.WriteAsync(domainEvent, ct);
        }

        channel.Writer.Complete();

        using var service = new DomainEventDispatcherService(channel, provider.GetRequiredService<IServiceScopeFactory>(), logger);
        await service.StartAsync(ct);
        await service.ExecuteTask!.WaitAsync(Budget, ct);
        await service.StopAsync(ct);
    }

    private sealed record SampleEvent(string Payload) : IDomainEvent;

    private sealed record SampleExternalEvent(string Payload) : IExternalEvent;

    private sealed record UnhandledEvent : IDomainEvent;

    private sealed class ScopeMarker;

    private sealed class HandlerLog
    {
        internal const string Throw = "throw";

        private readonly ConcurrentQueue<(string Handler, string Payload, ScopeMarker Scope)> _calls = new();

        internal IReadOnlyCollection<(string Handler, string Payload, ScopeMarker Scope)> Calls => _calls;

        internal Task RecordAsync(string handler, string payload, ScopeMarker scope)
        {
            if (payload == Throw)
                throw new InvalidOperationException("handler boom");

            _calls.Enqueue((handler, payload, scope));
            return Task.CompletedTask;
        }
    }

    private sealed class FirstSampleHandler(HandlerLog log, ScopeMarker scope) : IDomainEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent domainEvent, CancellationToken cancellationToken = default) =>
            log.RecordAsync("first", domainEvent.Payload, scope);
    }

    private sealed class SecondSampleHandler(HandlerLog log, ScopeMarker scope) : IDomainEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent domainEvent, CancellationToken cancellationToken = default) =>
            log.RecordAsync("second", domainEvent.Payload, scope);
    }

    private sealed class ExternalHandler(HandlerLog log, ScopeMarker scope) : IDomainEventHandler<SampleExternalEvent>
    {
        public Task HandleAsync(SampleExternalEvent domainEvent, CancellationToken cancellationToken = default) =>
            log.RecordAsync("external", domainEvent.Payload, scope);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        internal readonly record struct Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly ConcurrentQueue<Entry> _entries = new();

        internal IReadOnlyCollection<Entry> Entries => _entries;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue(new Entry(logLevel, formatter(state, exception), exception));
    }
}
