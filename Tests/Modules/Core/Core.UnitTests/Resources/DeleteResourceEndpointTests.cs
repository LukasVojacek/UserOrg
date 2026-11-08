using Core.Features.Resource.DeleteResource;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Resources;

/// <summary>
/// Handler tests for the resource delete endpoint (F4 Phase C; ADR-0026 / ADR-0030 / ADR-0041, decisions
/// C1/C3/C4). Exercises provider-write authorization, the (providerName, resourceName) → entity load by
/// natural key (ADR-0041 L2.4), and the load-then-soft-delete-through-the-aggregate path: 204 sets IsDeleted,
/// a single 404 for an unresolved key (missing / wrong-provider / already-deleted collapse to one path), 403.
/// </summary>
public sealed class DeleteResourceEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";
    private const string ResName = "res-1";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static Caller ProviderCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    // why: owns a DIFFERENT provider rather than none, so a 403 can only come from the ownership comparison - the whole gate for a non-Admin role since the identity kind left it (ADR-0105).
    private static Caller OtherProviderCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    // why: interactive rather than a service key, and allowed the write anyway - its provider claim alone decides (ADR-0105).
    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("pa"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: false);

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
        ResourceName.Create(ResName), Core.Domain.Provider.Provider.Create(ProviderName.From(Provider), countryId: 1), UserId.From(Guid.NewGuid()), Start, End);

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> Repo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Task<IResult> InvokeAsync(
        Caller caller, Mock<IRepository<ResourceAggregate, ProjectResourceKey>> repo, Mock<IUnitOfWork> unitOfWork)
        => DeleteResourceEndpoint.HandleAsync(
            Provider, ResName, caller, repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<DeleteResourceEndpoint.AuditCategory>(), NullLogger<DeleteResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContentAndSoftDelete_When_Valid()
    {
        var resource = ExistingResource();
        var repo = Repo(resource);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ProviderCaller(), repo, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.True(resource.IsDeleted);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_KeyUnresolved()
    {
        // FindAsync returns null for missing / wrong-provider / already-deleted alike (C3) — one 404.
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ProviderCaller(), Repo(null), unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("resource.not_found", problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbiddenAndNotResolve_When_NotAuthorized()
    {
        var repo = Repo(ExistingResource());
        var unitOfWork = SavingUnitOfWork();

        var result = await DeleteResourceEndpoint.HandleAsync(
            Provider, ResName, OtherProviderCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<DeleteResourceEndpoint.AuditCategory>(), NullLogger<DeleteResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
        repo.Verify(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndSoftDelete_When_CallerIsInteractiveOwningProviderAdmin()
    {
        var resource = ExistingResource();
        var repo = Repo(resource);
        var unitOfWork = SavingUnitOfWork();

        var result = await DeleteResourceEndpoint.HandleAsync(
            Provider, ResName, InteractiveProviderAdminCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<DeleteResourceEndpoint.AuditCategory>(), NullLogger<DeleteResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.True(resource.IsDeleted);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
