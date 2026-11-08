using Core.Domain.Provider;
using Core.Domain.Shared;

namespace Core.UnitTests.Providers;

/// <summary>
/// Unit tests for the ProjectResource aggregate (ADR-0026 / F4 Phase A). No mocks — pure domain.
/// </summary>
public sealed class ProjectResourceDomainTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static Provider OwningProvider(string name = "acme") =>
        Provider.Create(ProviderName.From(name), countryId: 1);

    [Fact]
    public void Should_SetOwningProvider_When_Created()
    {
        var provider = OwningProvider();

        var resource = ProjectResource.Create(ResourceName.Create("OPEN-33-82"), provider, UserId.From(Guid.NewGuid()), Start, End);

        Assert.Equal(provider.Name, resource.ProviderName);
        Assert.Equal("OPEN-33-82", resource.ResourceName.Value);
        Assert.False(resource.IsDeleted);
    }

    [Fact]
    public void Should_Throw_When_CreatedWithEndBeforeStart()
    {
        Assert.Throws<ArgumentException>(() =>
            ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), End, Start));
    }

    [Fact]
    public void Should_UpdateMetadata_When_UpdateDetailsCalled()
    {
        var resource = ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), Start, End);
        var newEnd = End.AddYears(1);

        resource.UpdateDetails(Start, newEnd, "updated desc", EOperationMode.ReadOnly, null, null, null);

        Assert.Equal("updated desc", resource.Description);
        Assert.Equal(newEnd, resource.EndDate);
        Assert.Equal(EOperationMode.ReadOnly, resource.OperationMode);
    }

    [Fact]
    public void Should_NotChangeIdentity_When_UpdateDetailsCalled()
    {
        var provider = OwningProvider();
        var resource = ProjectResource.Create(ResourceName.Create("res"), provider, UserId.From(Guid.NewGuid()), Start, End);

        resource.UpdateDetails(Start, End, "x", EOperationMode.Unspecified, null, null, null);

        // Name and owning provider are identity — UpdateDetails must not touch them (rename is disabled, ADR-0030).
        Assert.Equal("res", resource.ResourceName.Value);
        Assert.Equal(provider.Name, resource.ProviderName);
    }

    [Fact]
    public void Should_Throw_When_UpdateDetailsEndBeforeStart()
    {
        var resource = ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), Start, End);

        Assert.Throws<ArgumentException>(() =>
            resource.UpdateDetails(End, Start, null, EOperationMode.Unspecified, null, null, null));
    }

    [Fact]
    public void Should_MarkDeleted_When_Deleted()
    {
        var resource = ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), Start, End);

        resource.Delete();
        resource.UpdateState(EProjectResourceState.Deleted);

        Assert.True(resource.IsDeleted);
    }

    [Fact]
    public void Should_RaiseProjectResourceActivatedEvent_When_Create()
    {
        var provider = OwningProvider();

        var resource = ProjectResource.Create(ResourceName.Create("res"), provider, UserId.From(Guid.NewGuid()), Start, End);

        var evt = Assert.Single(resource.DomainEvents);
        var activated = Assert.IsType<ProjectResourceActivatedEvent>(evt);
        Assert.Equal(resource.Key, activated.Resource);
    }

    [Fact]
    public void Should_UpdateState_When_StateChanged()
    {
        var resource = ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), Start, End);
        Assert.Equal(EProjectResourceState.Draft, resource.State);

        resource.UpdateState(EProjectResourceState.Published);

        Assert.Equal(EProjectResourceState.Published, resource.State);
    }

    [Fact]
    public void Should_ReturnToDraft_When_UpdatedBackFromPublished()
    {
        var resource = ProjectResource.Create(ResourceName.Create("res"), OwningProvider(), UserId.From(Guid.NewGuid()), Start, End);
        resource.UpdateState(EProjectResourceState.Published);

        resource.UpdateState(EProjectResourceState.Draft);

        Assert.Equal(EProjectResourceState.Draft, resource.State);
    }
}
