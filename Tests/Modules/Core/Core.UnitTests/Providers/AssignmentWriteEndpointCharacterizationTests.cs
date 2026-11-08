using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.ResourceAssignment;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

using ProviderAggregate = Core.Domain.Provider.Provider;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="UpdateAssignmentEndpoint.HandleAsync"/> and
/// <see cref="RemoveAssignmentEndpoint.HandleAsync"/> (T-197-04): together with
/// <see cref="ResourceAssignmentEndpointTests"/> they pin every branch of both handlers as they stand before
/// the extract-method refactor, and the refactor does not edit them.
/// </summary>
public sealed class AssignmentWriteEndpointCharacterizationTests
{
    private const string Provider = "acme";
    private const string Location = "gpu";
    private const string ResName = "res-1";
    private const string Slug = "asg-res-1-test01";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static ProviderAggregate ProviderWithAssignment(EOperationMode resourceMode = EOperationMode.Editable)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://gpu", ELocationType.HPC);
        var resource = ResourceAggregate.Create(ResourceName.From(ResName), provider, UserId.From(Guid.NewGuid()), Start, End, operationMode: resourceMode);
        provider.AssignResource(
            LocationName.From(Location), resource, allocationUnitTypeId: (int)EAllocationUnitType.NodeHours,
            allocationUnitCount: 100, description: "d", aggregationName: "agg",
            specifications: [("cpu", "16"), ("ram", "64")], resourceHasNonSharedLocation: false, slug: Slug);
        provider.ClearDomainEvents();
        return provider;
    }

    private static AssignedLocation Assignment(ProviderAggregate provider) =>
        provider.Locations.Single().AssignedLocations.Single();

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindWithAllocatedResourcesAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<IResourceAssignmentQuery> AssignmentQuery(bool allocationTypeExists = true)
    {
        var query = new Mock<IResourceAssignmentQuery>();
        query.Setup(q => q.AllocationUnitTypeExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allocationTypeExists);
        return query;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static UpdateAssignmentEndpoint.Request UpdateRequest(
        IReadOnlyList<UpdateAssignmentEndpoint.SpecificationItem>? specifications) =>
        new(EAllocationUnitType.CoreHours, AllocationUnitCount: 250, Description: "d2", AggregationName: "agg2", specifications);

    private static Task<IResult> InvokeUpdateAsync(
        string locationName,
        string slug,
        UpdateAssignmentEndpoint.Request request,
        Mock<IProviderRepository> repo,
        Mock<IResourceAssignmentQuery> query,
        Mock<IUnitOfWork> unitOfWork) =>
        UpdateAssignmentEndpoint.HandleAsync(
            Provider, locationName, slug, request, OwnerCaller(), repo.Object, query.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateAssignmentEndpoint.AuditCategory>(), NullLogger<UpdateAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeRemoveAsync(
        string locationName,
        string slug,
        Mock<IProviderRepository> repo,
        Mock<IUnitOfWork> unitOfWork) =>
        RemoveAssignmentEndpoint.HandleAsync(
            Provider, locationName, slug, OwnerCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<RemoveAssignmentEndpoint.AuditCategory>(), NullLogger<RemoveAssignmentEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ApplyEveryFieldAndReplaceSpecifications_When_UpdateValid()
    {
        var provider = ProviderWithAssignment();
        var specifications = new List<UpdateAssignmentEndpoint.SpecificationItem> { new("gpu", "4") };

        var result = await InvokeUpdateAsync(Location, Slug, UpdateRequest(specifications), Repo(provider), AssignmentQuery(), SavingUnitOfWork());

        Assert.IsType<NoContent>(result);
        var assignment = Assignment(provider);
        Assert.Equal((int)EAllocationUnitType.CoreHours, assignment.AllocationUnitTypeId);
        Assert.Equal(250, assignment.AllocationUnitCount);
        Assert.Equal("d2", assignment.Description);
        Assert.Equal("agg2", assignment.AggregationName);
        var value = Assert.Single(assignment.Values);
        Assert.Equal(("gpu", "4"), (value.Name, value.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_ClearSpecifications_When_UpdateSendsNoneOrEmpty(bool sendEmptyList)
    {
        var provider = ProviderWithAssignment();
        var unitOfWork = SavingUnitOfWork();
        var specifications = sendEmptyList ? new List<UpdateAssignmentEndpoint.SpecificationItem>() : null;

        var result = await InvokeUpdateAsync(Location, Slug, UpdateRequest(specifications), Repo(provider), AssignmentQuery(), unitOfWork);

        Assert.IsType<NoContent>(result);
        Assert.Empty(Assignment(provider).Values);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_UpdateLocationNameBlank()
    {
        var repo = Repo(ProviderWithAssignment());
        var query = AssignmentQuery();

        var result = await InvokeUpdateAsync(" ", Slug, UpdateRequest(null), repo, query, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
        repo.VerifyNoOtherCalls();
        query.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnValidationAndNotLoad_When_UpdateSlugBlank(string slug)
    {
        var repo = Repo(ProviderWithAssignment());
        var query = AssignmentQuery();

        var result = await InvokeUpdateAsync(Location, slug, UpdateRequest(null), repo, query, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.Required);
        Assert.Equal("Assignment slug is required.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
        query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportLocationNameBeforeSlug_When_UpdateHasBothBlank()
    {
        var result = await InvokeUpdateAsync(" ", " ", UpdateRequest(null), Repo(ProviderWithAssignment()), AssignmentQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
    }

    [Fact]
    public async Task Should_ReportUnknownAllocationUnitTypeBeforeMissingProvider_When_Update()
    {
        var repo = Repo(provider: null);

        var result = await InvokeUpdateAsync(Location, Slug, UpdateRequest(null), repo, AssignmentQuery(allocationTypeExists: false), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, "provider.allocation_unit_type_not_found");
        Assert.Equal("Allocation unit type 'CoreHours' does not exist.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NameTheResourceInTheReadOnlyProblem_When_UpdateResourceReadOnly()
    {
        var result = await InvokeUpdateAsync(
            Location, Slug, UpdateRequest(null), Repo(ProviderWithAssignment(EOperationMode.ReadOnly)), AssignmentQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, ProviderErrorCodes.ProviderReadOnly);
        Assert.Equal($"Project resource '{ResName}' is in read-only mode and cannot be modified.", ((ProblemHttpResult)result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_UpdateLocationMissing()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeUpdateAsync("cpu", Slug, UpdateRequest(null), Repo(ProviderWithAssignment()), AssignmentQuery(), unitOfWork);

        AssertProblem(result, StatusCodes.Status404NotFound, ProviderErrorCodes.LocationNotFound);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_SoftDeleteTheAssignment_When_RemoveValid()
    {
        var provider = ProviderWithAssignment();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeRemoveAsync(Location, Slug, Repo(provider), unitOfWork);

        Assert.IsType<NoContent>(result);
        Assert.True(Assignment(provider).IsDeleted);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_RemoveLocationNameBlank()
    {
        var repo = Repo(ProviderWithAssignment());

        var result = await InvokeRemoveAsync(" ", Slug, repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
        repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnValidationAndNotLoad_When_RemoveSlugBlank(string slug)
    {
        var repo = Repo(ProviderWithAssignment());

        var result = await InvokeRemoveAsync(Location, slug, repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.Required);
        Assert.Equal("Assignment slug is required.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportLocationNameBeforeSlug_When_RemoveHasBothBlank()
    {
        var result = await InvokeRemoveAsync(" ", " ", Repo(ProviderWithAssignment()), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
    }

    [Fact]
    public async Task Should_NameTheResourceInTheReadOnlyProblem_When_RemoveResourceReadOnly()
    {
        var provider = ProviderWithAssignment(EOperationMode.ReadOnly);

        var result = await InvokeRemoveAsync(Location, Slug, Repo(provider), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, ProviderErrorCodes.ProviderReadOnly);
        Assert.Equal($"Project resource '{ResName}' is in read-only mode and cannot be modified.", ((ProblemHttpResult)result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_RemoveLocationMissing()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeRemoveAsync("cpu", Slug, Repo(ProviderWithAssignment()), unitOfWork);

        AssertProblem(result, StatusCodes.Status404NotFound, ProviderErrorCodes.LocationNotFound);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
