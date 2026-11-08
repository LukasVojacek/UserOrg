using Heappe.HostContracts;

using Userorg.Adapter.Heappe.Userorg;

using UserOrganizationManagement.Application.UserOrg.IServices;

namespace Heappe.Adapter.Tests;

/// <summary>
/// Pins the dependency direction that keeps the DI graph acyclic.
/// </summary>
/// <remarks>
/// Regression guard. The version resolver used to depend on <see cref="IUserorgService"/>. When the
/// kill-switch gave <see cref="UserorgService"/> Core's project/location/user services (so it could
/// resolve names to ids), that closed a cycle:
/// IProjectService -> IHeappeConnectionService -> IHeappeService -> IHeappeVersionResolver
/// -> IUserorgService -> IProjectService, and the API stopped starting.
///
/// Hand-constructed unit tests cannot see this — they never build the container — so these tests
/// assert the shape directly. They pin the constructor edge only; they are not a substitute for the
/// container's own validation, which is what actually catches a cycle introduced somewhere else.
/// </remarks>
public sealed class HeappeVersionSourceDependencyTests
{
    private static Type[] ConstructorDependenciesOf<T>() =>
        typeof(T).GetConstructors()
            .Single()
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

    [Fact]
    public void HeappeVersionSource_Should_not_depend_on_IUserorgService()
    {
        var dependencies = ConstructorDependenciesOf<HeappeVersionSource>();

        Assert.DoesNotContain(typeof(IUserorgService), dependencies);
    }

    [Fact]
    public void HeappeVersionSource_Should_depend_only_on_the_HEAppE_integration_service()
    {
        // Anything else from Core's service layer drags in the graph that leads back to
        // IUserorgService. Keeping this to the single dependency the version logic actually needs is
        // what keeps the resolver underneath that layer.
        var dependencies = ConstructorDependenciesOf<HeappeVersionSource>();

        Assert.Equal([typeof(IHeappeIntegrationService)], dependencies);
    }

    [Fact]
    public void HeappeVersionDatabaseResolver_Should_depend_on_the_narrow_version_contract()
    {
        // The edge that actually closed the cycle. The tests above pin HeappeVersionSource's own
        // dependencies, but the resolver is what CHOOSES between the narrow contract and the fat
        // one: point it back at IUserorgService and the cycle returns with every other test here
        // still green. Resolved by name because the resolver is internal to the Heappe module,
        // which does not expose its internals to this assembly.
        var resolver = typeof(IHeappeVersionSource).Assembly
            .GetType("Heappe.Features.VersionResolver.HeappeVersionDatabaseResolver", throwOnError: true)!;

        var dependencies = resolver.GetConstructors().Single().GetParameters().Select(p => p.ParameterType);

        Assert.Equal([typeof(IHeappeVersionSource)], dependencies);
    }

    [Fact]
    public void UserorgService_Should_still_be_the_type_that_reaches_into_Core()
    {
        // Guards the premise of the test above: UserorgService is the fat host contract, and sharing
        // it with the version resolver is precisely what caused the cycle. If this ever stops being
        // true the split has been undone or reshaped, and the tests above would silently pass for
        // the wrong reason.
        var dependencies = ConstructorDependenciesOf<UserorgService>();

        Assert.Contains(typeof(IProjectService), dependencies);
    }
}
