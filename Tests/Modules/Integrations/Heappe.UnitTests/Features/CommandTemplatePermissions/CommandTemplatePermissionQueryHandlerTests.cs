using Heappe.Contracts.CommandTemplatePermissions.Contract;
using Heappe.Features.CommandTemplatePermissions;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CommandTemplatePermissions;

/// <summary>
/// Verifies caching of the expensive per-user HEAppE allocation lookup behind the permission query.
/// Allocations are returned empty so the handler short-circuits before the EF projection (unsupported by
/// the in-memory provider); the allocation cache layer is still fully exercised.
/// </summary>
public sealed class CommandTemplatePermissionQueryHandlerTests
{
    private const string IdpUserId = "user-1";
    private const string HeappeIdentifier = "heappe-1";

    private readonly Mock<IUserorgService> _userService = new(MockBehavior.Strict);
    private readonly FakeHybridCache _cache = new();

    [Fact(DisplayName = "Should_serve_allocations_from_cache_When_called_twice")]
    public async Task Should_serve_allocations_from_cache_When_called_twice()
    {
        SetupAllocations();
        using var dbContext = CreateInMemoryDbContext();
        var handler = new CommandTemplatePermissionQueryHandler(dbContext, _userService.Object, _cache, new KillSwitchStateService(dbContext));
        var query = new CommandTemplatePermissionQuery(IdpUserId, HeappeIdentifier);

        await handler.Handle(query, CancellationToken.None);
        await handler.Handle(query, CancellationToken.None);

        _userService.Verify(
            s => s.GetUserProjectAllocationsAsync(IdpUserId, HeappeIdentifier, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "Should_refetch_allocations_When_heappe_tag_evicted")]
    public async Task Should_refetch_allocations_When_heappe_tag_evicted()
    {
        SetupAllocations();
        using var dbContext = CreateInMemoryDbContext();
        var handler = new CommandTemplatePermissionQueryHandler(dbContext, _userService.Object, _cache, new KillSwitchStateService(dbContext));
        var query = new CommandTemplatePermissionQuery(IdpUserId, HeappeIdentifier);

        await handler.Handle(query, CancellationToken.None);
        await _cache.RemoveByTagAsync(HeappeCacheKeys.PermissionHeappeTag(HeappeIdentifier), CancellationToken.None);
        await handler.Handle(query, CancellationToken.None);

        _userService.Verify(
            s => s.GetUserProjectAllocationsAsync(IdpUserId, HeappeIdentifier, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private void SetupAllocations()
        => _userService
            .Setup(s => s.GetUserProjectAllocationsAsync(IdpUserId, HeappeIdentifier, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<ProjectAllocationInfoEntry>, Error>.Success([], State.Ok));

    private static HeappeIntegrationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"TestDb_{Guid.NewGuid()}")
            .Options;

        return new HeappeIntegrationDbContext(options);
    }
}
