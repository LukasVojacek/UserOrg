using Core.Domain.Project;
using Core.Domain.Provider;

using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CommandTemplateSynchronization;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Heappe;

namespace Heappe.Adapter.Tests;

/// <summary>
/// Verifies <see cref="HeappeResourceSyncRunner"/> runs the HEAppE sync to completion (ADR-0040): it returns
/// on a successful <see cref="IProjectCommandTemplateSyncService"/> result and throws on failure, so the
/// outbox dispatcher leaves the row undispatched and retries.
/// </summary>
public sealed class HeappeResourceSyncRunnerTests
{
    [Fact]
    public async Task Should_Complete_When_SyncSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var succeeded = new CommandTemplateSyncResult(
            [new LocationSyncResultEntry("loc-a", null, new Uri("https://heappe.test"), IsSuccess: true, [])]);
        var sync = new FakeSyncService(ValidationResult<CommandTemplateSyncResult>.Success(succeeded));
        var runner = new HeappeResourceSyncRunner(NullLogger<HeappeResourceSyncRunner>.Instance, sync);

        await runner.RunResourceSyncAsync(new ProjectShortName("proj-a"), ResourceName.Create("gpu-res"), ct);

        Assert.Equal(1, sync.Calls);
    }

    [Fact]
    public async Task Should_Throw_When_SyncFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var failure = ValidationResult<CommandTemplateSyncResult>.Failure([new Error("HEAppE.SyncFailed", ErrorSeverity.Error, "boom")]);
        var sync = new FakeSyncService(failure);
        var runner = new HeappeResourceSyncRunner(NullLogger<HeappeResourceSyncRunner>.Instance, sync);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunResourceSyncAsync(new ProjectShortName("proj-a"), ResourceName.Create("gpu-res"), ct));
    }

    private sealed class FakeSyncService(ValidationResult<CommandTemplateSyncResult> result) : IProjectCommandTemplateSyncService
    {
        public int Calls { get; private set; }

        public Task<ValidationResult<CommandTemplateSyncResult>> SyncCommandTemplatesForResource(string projectShortName, string projectResourceName, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
