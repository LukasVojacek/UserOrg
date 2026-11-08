using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.EndToEnd;

/// <summary>
/// Drives the whole engine — submit, worker dispatch, gate parking, resume — over an in-memory repository
/// and the in-process channel queue. This is the integration oracle for the worker's gate-parking branch.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GatedFlowEndToEndTests
{
    private sealed record StepA(Guid RequestId) : StepRequest(RequestId, nameof(StepA));
    private sealed record StepB(Guid RequestId) : StepRequest(RequestId, nameof(StepB));
    private sealed record Approval(bool Accept);

    private sealed class NoopHandler<T> : IStepHandler<T> where T : StepRequest
    {
        public Task<StepResult> HandleAsync(T request, CancellationToken ct) => Task.FromResult(new StepResult(null));
    }

    private sealed class GatedFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("e2e-gated")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("ApprovalGate")
            .From<StepA>().GoToGate("ApprovalGate")
            .FromGate<Approval>("ApprovalGate").Branch(c => c
                .When(a => a.Accept, (a, snap) => new StepB(snap.RequestId))
                .OtherwiseTerminal())
            .From<StepB>().Terminal();
    }

    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact(DisplayName = "Gated flow parks at the gate on start, then resume drives it to completion")]
    public async Task Should_ParkThenResumeToCompletion_When_GatedFlowRun()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IRequestRepository, InMemoryRequestRepository>();
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();
        services.AddFlow<GatedFlow>();

        await using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<IRequestRepository>();
        var workers = provider.GetServices<IHostedService>().ToList();

        foreach (var worker in workers)
        {
            await worker.StartAsync(CancellationToken.None);
        }

        try
        {
            var id = await StartAsync(provider);

            await WaitForStatusAsync(repository, id, RequestStatus.AwaitingSignal);
            var parked = await repository.FindByIdAsync(id);
            Assert.Equal("ApprovalGate", parked!.CurrentState);

            var resume = await ResumeAsync(provider, id);
            Assert.Equal(ResumeOutcome.Resumed, resume.Outcome);

            await WaitForStatusAsync(repository, id, RequestStatus.Completed);
        }
        finally
        {
            foreach (var worker in workers)
            {
                await worker.StopAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<Guid> StartAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
        return await client.StartAsync<GatedFlow>(new object());
    }

    private static async Task<ResumeResult> ResumeAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IWorkflowClient>();
        return await client.ResumeAsync(id, new Approval(Accept: true));
    }

    private static async Task WaitForStatusAsync(IRequestRepository repository, Guid id, RequestStatus expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var aggregate = await repository.FindByIdAsync(id);
            if (aggregate?.Status == expected)
            {
                return;
            }

            await Task.Delay(25);
        }

        var actual = (await repository.FindByIdAsync(id).ConfigureAwait(false))?.Status;
        Assert.Fail($"Request {id} did not reach {expected} within the timeout (last status: {actual}).");
    }
}
