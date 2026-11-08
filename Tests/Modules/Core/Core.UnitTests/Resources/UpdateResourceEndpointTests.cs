using Core.Domain.Shared;
using Core.Features.Resource.UpdateResource;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Resources;

/// <summary>
/// Handler tests for the resource update endpoint (F4 Phase C; ADR-0026 / ADR-0030 / ADR-0041, decisions
/// C1–C4). Exercises validation, provider-write authorization, the (providerName, resourceName) → entity load
/// by natural key (ADR-0041 L2.4), and the load-then-mutate-through-the-aggregate path: a 204 that changes
/// metadata while preserving the non-contract operationMode / externalId (C2), a single 404 for an unresolved
/// key (missing / wrong-provider / soft-deleted collapse to one path here), 403, 400.
/// </summary>
public sealed class UpdateResourceEndpointTests
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

    // A service key on purpose, owning a DIFFERENT provider: the 403 has to come from the ownership
    // comparison. An interactive caller here would be denied by D9's identity-kind gate before ownership
    // is ever compared, and the test would pass against a build that had lost the ownership check.
    private static Caller OtherProviderCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    // An existing resource carrying a non-contract operationMode + externalId, to prove they survive the update.
    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
        ResourceName.Create(ResName), Core.Domain.Provider.Provider.Create(ProviderName.From(Provider), countryId: 1), UserId.From(Guid.NewGuid()), Start, End,
        description: "old", operationMode: EOperationMode.Editable,
        primaryInvestigatorEmail: Email.Create("old@example.com"), externalId: ExternalId.Create("ext-1"));

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
        Caller caller, Mock<IRepository<ResourceAggregate, ProjectResourceKey>> repo,
        Mock<IUnitOfWork> unitOfWork, UpdateResourceEndpoint.Request? request = null)
    {
        request ??= new UpdateResourceEndpoint.Request("new-desc", "pi@example.com", Start, End, null);

        return UpdateResourceEndpoint.HandleAsync(
            Provider, ResName, request, caller, repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateResourceEndpoint.AuditCategory>(), NullLogger<UpdateResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContentAndPreserveNonContractFields_When_Valid()
    {
        var resource = ExistingResource();
        var repo = Repo(resource);
        var unitOfWork = SavingUnitOfWork();
        var newEnd = End.AddYears(1);

        var request = new UpdateResourceEndpoint.Request("new-desc", "pi@example.com", Start, newEnd, null);
        var result = await InvokeAsync(ProviderCaller(), repo, unitOfWork, request);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        // Mutated through the aggregate: contract fields updated…
        Assert.Equal("new-desc", resource.Description);
        Assert.Equal(newEnd, resource.EndDate);
        Assert.Equal("pi@example.com", resource.PrimaryInvestigatorEmail!.Value.Value);
        // …non-contract fields preserved (C2).
        Assert.Equal(EOperationMode.Editable, resource.OperationMode);
        Assert.Equal("ext-1", resource.ExternalId!.Value.Value);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_KeyUnresolved()
    {
        // FindAsync returns null for missing / wrong-provider / soft-deleted alike (C3) — one 404 path.
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

        var request = new UpdateResourceEndpoint.Request("new-desc", "pi@example.com", Start, End, null);
        var result = await UpdateResourceEndpoint.HandleAsync(
            Provider, ResName, request, OtherProviderCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateResourceEndpoint.AuditCategory>(), NullLogger<UpdateResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
        repo.Verify(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidation_When_PrimaryInvestigatorEmailMissing()
    {
        var request = new UpdateResourceEndpoint.Request("new-desc", "", Start, End, null);

        var result = await InvokeAsync(ProviderCaller(), Repo(ExistingResource()), SavingUnitOfWork(), request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidation_When_EndDateNotAfterStartDate()
    {
        var request = new UpdateResourceEndpoint.Request("new-desc", "pi@example.com", End, Start, null);

        var result = await InvokeAsync(ProviderCaller(), Repo(ExistingResource()), SavingUnitOfWork(), request);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
    }
}
