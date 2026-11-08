using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Workers;

/// <summary>
/// A step's row is created by the worker itself, so on its first execution there is no row to lease and two
/// workers can both reach the claim. The aggregate's rowversion settles it — and the loser must fold before
/// the handler runs, or the effect (an e-mail, an attach) happens twice.
/// </summary>
[Trait("Category", "Integration")]
public sealed class StepClaimRaceTests
{
    private sealed record OnlyStep(Guid RequestId) : StepRequest(RequestId, nameof(OnlyStep));

    private sealed class CountingHandler : IStepHandler<OnlyStep>
    {
        // The engine resolves handlers by type from DI, so the count lives on the type.
        private static int _invocations;

        public static int Invocations => Volatile.Read(ref _invocations);

        public static void Reset() => Volatile.Write(ref _invocations, 0);

        public Task<StepResult> HandleAsync(OnlyStep request, CancellationToken ct)
        {
            Interlocked.Increment(ref _invocations);
            return Task.FromResult(new StepResult(null));
        }
    }

    private sealed class SingleStepFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new OnlyStep(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("claim-race")
            .Step<OnlyStep, CountingHandler>()
            .From<OnlyStep>().Terminal();
    }

    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Stands in for rowversion: saving the named request fails as if another worker claimed it first.</summary>
    private sealed class LosingRepository : InMemoryRequestRepository
    {
        public Guid LosesClaimFor { get; set; }

        public override Task SaveAsync(RequestAggregate aggregate, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(aggregate);

            return aggregate.RequestId == LosesClaimFor
                ? throw new DbUpdateConcurrencyException("Another worker claimed the step first.")
                : base.SaveAsync(aggregate, ct);
        }
    }

    [Fact(DisplayName = "Losing the claim skips the handler and leaves the worker consuming")]
    public async Task Should_Skip_Handler_And_Keep_Worker_Alive_When_Claim_Is_Lost()
    {
        CountingHandler.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<LosingRepository>();
        services.AddSingleton<IRequestRepository>(sp => sp.GetRequiredService<LosingRepository>());
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<SingleStepFlow>();

        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<LosingRepository>();
        var queue = provider.GetRequiredService<IQueuePort>();
        var workers = provider.GetServices<IHostedService>().ToList();

        var lost = NewRequest();
        var later = NewRequest();
        await repository.AddAsync(lost);
        await repository.AddAsync(later);
        repository.LosesClaimFor = lost.RequestId;

        foreach (var worker in workers)
        {
            await worker.StartAsync(CancellationToken.None);
        }

        try
        {
            await queue.EnqueueAsync(new OnlyStep(lost.RequestId), CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(200));

            Assert.Equal(0, CountingHandler.Invocations);

            // An unhandled concurrency exception escapes the consume loop and takes the worker down with
            // it — so the proof that the loser folded quietly is that the next step still gets dispatched.
            await queue.EnqueueAsync(new OnlyStep(later.RequestId), CancellationToken.None);
            await WaitForInvocationAsync();
        }
        finally
        {
            foreach (var worker in workers)
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }

        Assert.Equal(1, CountingHandler.Invocations);
    }

    // The worker consumes on its own thread; poll rather than guess a delay that holds under a full test run.
    private static async Task WaitForInvocationAsync()
    {
        for (var i = 0; i < 100 && CountingHandler.Invocations == 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static RequestAggregate NewRequest() => RequestAggregate.Create(
        "claim-race",
        System.Text.Json.JsonDocument.Parse("{}"),
        clientId: null,
        callbackUrl: null,
        idempotencyKey: null,
        now: DateTimeOffset.UtcNow);
}
