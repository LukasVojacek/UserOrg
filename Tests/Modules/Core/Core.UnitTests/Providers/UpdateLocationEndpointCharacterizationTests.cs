using Core.Domain.Shared;
using Core.Features.Provider.UpdateLocation;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="UpdateLocationEndpoint.HandleAsync"/> (T-197-04): they pin every
/// branch of the handler as it stands before the extract-method refactor, and the refactor does not edit them.
/// </summary>
public sealed class UpdateLocationEndpointCharacterizationTests
{
    private const string Provider = "acme";
    private const string Location = "gpu";

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static ProviderAggregate ProviderWithLocation(bool deleted = false)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        provider.AddLocation(LocationName.From(Location), "https://old", ELocationType.HPC, description: "old");
        if (deleted)
            provider.RemoveLocation(LocationName.From(Location));
        return provider;
    }

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Task<IResult> InvokeAsync(
        string locationName,
        UpdateLocationEndpoint.Request request,
        Mock<IProviderRepository> repo,
        Mock<IUnitOfWork> unitOfWork) =>
        UpdateLocationEndpoint.HandleAsync(
            Provider, locationName, request, OwnerCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateLocationEndpoint.AuditCategory>(), NullLogger<UpdateLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndUpdateLocation_When_RequestValid()
    {
        var provider = ProviderWithLocation();
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(Location, new("https://new", "new"), Repo(provider), unitOfWork);

        Assert.IsType<NoContent>(result);
        var location = provider.Locations.Single();
        Assert.Equal("https://new", location.Endpoint);
        Assert.Equal("new", location.Description);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_AcceptBoundaryLengths_When_EndpointIs200AndDescriptionIs150()
    {
        var provider = ProviderWithLocation();
        var endpoint = new string('e', 200);
        var description = new string('d', 150);

        var result = await InvokeAsync(Location, new(endpoint, description), Repo(provider), SavingUnitOfWork());

        Assert.IsType<NoContent>(result);
        Assert.Equal(endpoint, provider.Locations.Single().Endpoint);
        Assert.Equal(description, provider.Locations.Single().Description);
    }

    [Fact]
    public async Task Should_ClearDescription_When_DescriptionNull()
    {
        var provider = ProviderWithLocation();

        var result = await InvokeAsync(Location, new("https://new", null), Repo(provider), SavingUnitOfWork());

        Assert.IsType<NoContent>(result);
        Assert.Null(provider.Locations.Single().Description);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_LocationNameBlank()
    {
        var repo = Repo(ProviderWithLocation());

        var result = await InvokeAsync(" ", new("https://new", null), repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
        repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnValidationAndNotLoad_When_EndpointBlank(string endpoint)
    {
        var repo = Repo(ProviderWithLocation());

        var result = await InvokeAsync(Location, new(endpoint, null), repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidEndpoint);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_EndpointOver200Characters()
    {
        var repo = Repo(ProviderWithLocation());

        var result = await InvokeAsync(Location, new(new string('e', 201), null), repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidEndpoint);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_DescriptionOver150Characters()
    {
        var repo = Repo(ProviderWithLocation());

        var result = await InvokeAsync(Location, new("https://new", new string('d', 151)), repo, SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 150 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportLocationNameBeforeEndpoint_When_BothInvalid()
    {
        var result = await InvokeAsync(" ", new(" ", new string('d', 151)), Repo(ProviderWithLocation()), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidLocationName);
    }

    [Fact]
    public async Task Should_ReportEndpointBeforeDescription_When_BothInvalid()
    {
        var result = await InvokeAsync(Location, new(" ", new string('d', 151)), Repo(ProviderWithLocation()), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidEndpoint);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_LocationMissing()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync("cpu", new("https://new", null), Repo(ProviderWithLocation()), unitOfWork);

        AssertProblem(result, StatusCodes.Status404NotFound, ProviderErrorCodes.LocationNotFound);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnConflictAndNotSave_When_LocationDeleted()
    {
        var provider = ProviderWithLocation(deleted: true);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(Location, new("https://new", "new"), Repo(provider), unitOfWork);

        AssertProblem(result, StatusCodes.Status409Conflict, ProviderErrorCodes.LocationAlreadyDeleted);
        Assert.Equal("https://old", provider.Locations.Single().Endpoint);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
