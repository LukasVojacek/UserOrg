using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.AddLocation;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="AddLocationEndpoint.HandleAsync"/> (T-197-04): together with
/// <see cref="ProviderWriteEndpointTests"/> they pin every branch of the handler as it stands before the
/// extract-method refactor, and the refactor does not edit them.
/// </summary>
public sealed class AddLocationEndpointCharacterizationTests
{
    private const string Provider = "acme";

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static ProviderAggregate EmptyProvider() => ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<ILocationQuery> LocationQuery()
    {
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.LocationTypeExistsAsync(It.IsAny<ELocationType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return query;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static AddLocationEndpoint.Request Request(
        string name = "gpu",
        string endpoint = "https://gpu",
        ELocationType locationType = ELocationType.HPC,
        string? description = null) =>
        new(name, endpoint, locationType, IsPrivate: true, IsShared: false, description);

    private static Task<IResult> InvokeAsync(
        AddLocationEndpoint.Request request,
        Mock<IProviderRepository> repo,
        Mock<ILocationQuery> locationQuery,
        Mock<IUnitOfWork> unitOfWork) =>
        AddLocationEndpoint.HandleAsync(
            Provider, request, OwnerCaller(), repo.Object, locationQuery.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_StoreEveryRequestedField_When_RequestValid()
    {
        var provider = EmptyProvider();
        var endpoint = new string('e', 200);
        var description = new string('d', 150);

        var result = await InvokeAsync(
            Request(endpoint: endpoint, description: description), Repo(provider), LocationQuery(), SavingUnitOfWork());

        Assert.IsType<Created<AddLocationEndpoint.Response>>(result);
        var location = provider.Locations.Single();
        Assert.Equal("gpu", location.Name.Value);
        Assert.Equal(endpoint, location.Endpoint);
        Assert.Equal(ELocationType.HPC, location.LocationType);
        Assert.True(location.IsPrivate);
        Assert.False(location.IsShared);
        Assert.Equal(description, location.Description);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_EndpointOver200Characters()
    {
        var repo = Repo(EmptyProvider());

        var result = await InvokeAsync(Request(endpoint: new string('e', 201)), repo, LocationQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidEndpoint);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_DescriptionOver150Characters()
    {
        var repo = Repo(EmptyProvider());

        var result = await InvokeAsync(Request(description: new string('d', 151)), repo, LocationQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 150 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportLocationNameBeforeEndpointAndDescription_When_AllInvalid()
    {
        var result = await InvokeAsync(
            Request(name: " ", endpoint: " ", description: new string('d', 151)), Repo(EmptyProvider()), LocationQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
    }

    [Fact]
    public async Task Should_ReportEndpointBeforeDescription_When_BothInvalid()
    {
        var result = await InvokeAsync(
            Request(endpoint: " ", description: new string('d', 151)), Repo(EmptyProvider()), LocationQuery(), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidEndpoint);
    }

    [Theory]
    [InlineData(ELocationType.Unspecified)]
    [InlineData((ELocationType)999)]
    public async Task Should_NotQueryLocationType_When_TypeUnspecifiedOrUndefined(ELocationType locationType)
    {
        var locationQuery = LocationQuery();

        var result = await InvokeAsync(Request(locationType: locationType), Repo(EmptyProvider()), locationQuery, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationType);
        locationQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnConflictAndNotSave_When_ActiveLocationWithSameNameExists()
    {
        var provider = EmptyProvider();
        provider.AddLocation(LocationName.From("gpu"), "https://existing", ELocationType.HPC);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(Request(), Repo(provider), LocationQuery(), unitOfWork);

        AssertProblem(result, StatusCodes.Status409Conflict, ProviderErrorCodes.LocationAlreadyExists);
        Assert.Equal("https://existing", provider.Locations.Single().Endpoint);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
