using System.Runtime.CompilerServices;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;
using Userorg.Adapter.Waldur.Sync;

using UserOrganizationManagement.Application.Abstraction;

using Waldur.Adapter.Tests.Fakes;
using Waldur.Contracts;
using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for the B06 consumer seam: the in-process publisher (scope-per-event dispatch by
/// concrete event type) and the idempotent service-account bootstrap.
/// </summary>
public sealed class WaldurEventConsumerTests
{

    /// <summary>Shared sink capturing which handler saw which event, plus the scoped DbContext.</summary>
    private sealed class HandlerRecorder
    {
        public List<(WaldurIntegrationEvent Event, IUserOrgDbContext Context)> Handled { get; } = [];
    }

    private sealed class RecordingProjectCreatedHandler(IUserOrgDbContext context, HandlerRecorder recorder)
        : IWaldurEventHandler<ProjectCreated>
    {
        public Task<Result<Error>> HandleAsync(ProjectCreated evt, CancellationToken ct)
        {
            recorder.Handled.Add((evt, context));
            return Task.FromResult(Result<Error>.Success());
        }
    }

    private sealed class RecordingProjectUpdatedHandler(IUserOrgDbContext context, HandlerRecorder recorder)
        : IWaldurEventHandler<ProjectUpdated>
    {
        public Task<Result<Error>> HandleAsync(ProjectUpdated evt, CancellationToken ct)
        {
            recorder.Handled.Add((evt, context));
            return Task.FromResult(Result<Error>.Success());
        }
    }

    private sealed class ThrowingProjectCreatedHandler : IWaldurEventHandler<ProjectCreated>
    {
        public Task<Result<Error>> HandleAsync(ProjectCreated evt, CancellationToken ct) =>
            throw new InvalidOperationException("handler failed");
    }

    private static ProjectPayload Payload(string externalId) =>
        new(externalId, "Name", "short", null, DateTime.UnixEpoch, DateTime.UnixEpoch, false, []);

    private static ProjectCreated Created(string externalId) =>
        new(Payload(externalId)) { Instance = "EFP", ServiceName = "EFP-Waldur" };

    private static ProjectUpdated Updated(string externalId) =>
        new(Payload(externalId)) { Instance = "EFP", ServiceName = "EFP-Waldur" };

    private static ServiceProvider BuildProvider(HandlerRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddWaldurEventConsumers();
        services.AddWaldurEventHandler<ProjectCreated, RecordingProjectCreatedHandler>();
        services.AddWaldurEventHandler<ProjectUpdated, RecordingProjectUpdatedHandler>();
        return services.BuildServiceProvider();
    }

    [Fact(DisplayName = "PublishAsync resolves the handler for the concrete event type and awaits it")]
    public async Task Should_ResolveHandlerForConcreteEventType_When_Published()
    {
        var recorder = new HandlerRecorder();
        using var sp = BuildProvider(recorder);
        var publisher = sp.GetRequiredService<IWaldurEventPublisher>();

        var created = Created("proj-1");
        var updated = Updated("proj-2");

        await publisher.PublishAsync(created, CancellationToken.None);
        await publisher.PublishAsync(updated, CancellationToken.None);

        Assert.Collection(
            recorder.Handled,
            h => Assert.Same(created, h.Event),
            h => Assert.Same(updated, h.Event));
    }

    [Fact(DisplayName = "PublishAsync opens a fresh DI scope per event (distinct DbContext instances)")]
    public async Task Should_UseFreshScopePerEvent_When_MultipleEventsPublished()
    {
        var recorder = new HandlerRecorder();
        using var sp = BuildProvider(recorder);
        var publisher = sp.GetRequiredService<IWaldurEventPublisher>();

        await publisher.PublishAsync(Created("proj-1"), CancellationToken.None);
        await publisher.PublishAsync(Created("proj-2"), CancellationToken.None);

        Assert.Equal(2, recorder.Handled.Count);
        Assert.NotSame(recorder.Handled[0].Context, recorder.Handled[1].Context);
    }

    // ── Handler throw propagates (not acked) ───────────────────────────────────

    [Fact(DisplayName = "PublishAsync propagates the handler exception (not swallowed = not acked)")]
    public async Task Should_PropagateException_When_HandlerThrows()
    {
        var recorder = new HandlerRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);
        services.AddScoped<IUserOrgDbContext, FakeUserOrgDbContext>();
        services.AddWaldurEventConsumers();
        services.AddWaldurEventHandler<ProjectCreated, ThrowingProjectCreatedHandler>();
        using var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IWaldurEventPublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(Created("proj-1"), CancellationToken.None));
    }

    [Fact(DisplayName = "PublishAsync has a dispatch arm for every concrete WaldurIntegrationEvent type")]
    public async Task Should_HaveDispatchArm_When_AnyContractEventTypeIsPublished()
    {
        var eventTypes = typeof(WaldurIntegrationEvent).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(WaldurIntegrationEvent).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(eventTypes);

        var services = new ServiceCollection();
        services.AddWaldurEventConsumers();
        using var sp = services.BuildServiceProvider();
        var publisher = sp.GetRequiredService<IWaldurEventPublisher>();

        foreach (var eventType in eventTypes)
        {
            var evt = (WaldurIntegrationEvent)RuntimeHelpers.GetUninitializedObject(eventType);

            var thrown = await Assert.ThrowsAnyAsync<Exception>(
                () => publisher.PublishAsync(evt, CancellationToken.None));

            Assert.IsType<InvalidOperationException>(thrown);
        }
    }

    private static readonly WaldurServiceAccountIdentity Identity =
        new("EFP-Waldur", "EFP-Waldur_provider", "efp.waldur@service.provider");

    [Fact(DisplayName = "EnsureServiceAccountAsync creates the service account when absent")]
    public async Task Should_CreateServiceAccount_When_Absent()
    {
        using var context = new FakeUserOrgDbContext();

        var result = await context.InitializeSystemExportUser(NullLogger.Instance, Identity, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Identity.ServiceAccountUserName, result.Value.UserName);
        Assert.Equal(1, await context.UsersQuery.CountAsync(u => u.IdentityProviderUserId == Identity.ServiceName));
    }

    [Fact(DisplayName = "EnsureServiceAccountAsync is idempotent (second call creates nothing new)")]
    public async Task Should_BeIdempotent_When_CalledTwice()
    {
        using var context = new FakeUserOrgDbContext();

        var first = await context.InitializeSystemExportUser(NullLogger.Instance, Identity, CancellationToken.None);
        var second = await context.InitializeSystemExportUser(NullLogger.Instance, Identity, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal(1, await context.UsersQuery.CountAsync(u => u.IdentityProviderUserId == Identity.ServiceName));
    }
}
