using Core.DataAccess.Outbox;
using Core.Domain.Project;
using Core.Domain.Provider;

namespace Core.UnitTests.Outbox;

/// <summary>
/// Unit tests for the stored-type → CLR-type resolver (block O4). An unknown type is a poison message and
/// must surface as a clear exception rather than be silently dropped.
/// </summary>
public sealed class OutboxEventTypeResolverTests
{
    private readonly OutboxEventTypeResolver _resolver = new();

    [Fact]
    public void Should_ResolveResourceAssignmentChangedEvent_When_FullNameProvided()
    {
        var resolved = _resolver.Resolve(typeof(ResourceAssignmentChangedEvent).FullName!);

        Assert.Equal(typeof(ResourceAssignmentChangedEvent), resolved);
    }

    [Fact]
    public void Should_ResolveProjectResourceAssignmentChangedEvent_When_FullNameProvided()
    {
        var resolved = _resolver.Resolve(typeof(ProjectResourceAssignmentChangedEvent).FullName!);

        Assert.Equal(typeof(ProjectResourceAssignmentChangedEvent), resolved);
    }

    [Fact]
    public void Should_Throw_When_TypeNameUnknown()
    {
        Assert.Throws<InvalidOperationException>(
            () => _resolver.Resolve("Core.Domain.Does.Not.Exist"));
    }
}
