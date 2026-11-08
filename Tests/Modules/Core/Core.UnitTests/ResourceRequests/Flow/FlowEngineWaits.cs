using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// Polls the engine's request repository while the real worker drives the flow on its own threads — the only way
/// an engine-level test can synchronise on progress. Fails the test with the last observed status/state rather
/// than timing out silently.
/// </summary>
internal static class FlowEngineWaits
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    internal static async Task WaitForStatusAsync(IRequestRepository repository, Guid id, RequestStatus expected)
    {
        var (reached, last) = await PollAsync(repository, id, aggregate => aggregate.Status == expected);
        if (!reached)
        {
            Assert.Fail($"Request {id} did not reach {expected} within the timeout (last status: {last?.Status}).");
        }
    }

    internal static async Task WaitForGateAsync(IRequestRepository repository, Guid id, string gateName)
    {
        var (parked, last) = await PollAsync(
            repository,
            id,
            aggregate => aggregate is { Status: RequestStatus.AwaitingSignal } && aggregate.CurrentState == gateName);

        if (!parked)
        {
            Assert.Fail($"Request {id} did not park at gate '{gateName}' (last status: {last?.Status}, state: {last?.CurrentState}).");
        }
    }

    private static async Task<(bool Satisfied, RequestAggregate? Last)> PollAsync(
        IRequestRepository repository, Guid id, Func<RequestAggregate, bool> satisfied)
    {
        // Taken from the ambient xUnit v3 context rather than a parameter so the existing WaitFor* call sites keep
        // their signatures; a cancelled run then unwinds here instead of sitting out the full Timeout.
        var ct = TestContext.Current.CancellationToken;
        var deadline = DateTimeOffset.UtcNow.Add(Timeout);
        RequestAggregate? aggregate;
        do
        {
            aggregate = await repository.FindByIdAsync(id, ct);
            if (aggregate is not null && satisfied(aggregate))
            {
                return (true, aggregate);
            }

            await Task.Delay(PollInterval, ct);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return (false, aggregate);
    }
}
