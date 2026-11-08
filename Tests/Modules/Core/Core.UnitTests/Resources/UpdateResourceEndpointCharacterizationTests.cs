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
/// Characterization tests for <see cref="UpdateResourceEndpoint.HandleAsync"/> (T-197-04): together with
/// <see cref="UpdateResourceEndpointTests"/> they pin every branch of the handler as it stands before the
/// extract-method refactor, and the refactor does not edit them. Unlike the provider writes, this handler
/// validates the whole request before it authorizes.
/// </summary>
public sealed class UpdateResourceEndpointCharacterizationTests
{
    private const string Provider = "acme";
    private const string ResName = "res-1";
    private const string PiEmail = "pi@example.com";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    private static Caller ForeignCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

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

    private static UpdateResourceEndpoint.Request Request(
        string? description = "new-desc",
        string email = PiEmail,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string? licenseUrl = "https://license") =>
        new(description, email, start ?? Start, end ?? End, licenseUrl);

    private static Task<IResult> InvokeAsync(
        UpdateResourceEndpoint.Request request,
        Mock<IRepository<ResourceAggregate, ProjectResourceKey>> repo,
        Caller? caller = null,
        string providerName = Provider,
        string resourceName = ResName) =>
        UpdateResourceEndpoint.HandleAsync(
            providerName, resourceName, request, caller ?? OwnerCaller(), repo.Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateResourceEndpoint.AuditCategory>(), NullLogger<UpdateResourceEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_LoadByNaturalKeyAndApplyLicenseAndStartDate_When_RequestValid()
    {
        var resource = ExistingResource();
        var repo = Repo(resource);
        var newStart = Start.AddDays(10);

        var result = await InvokeAsync(Request(start: newStart), repo);

        Assert.IsType<NoContent>(result);
        Assert.Equal(newStart, resource.StartDate);
        Assert.Equal("https://license", resource.LicenseUrl);
        repo.Verify(r => r.FindAsync(
            It.Is<ProjectResourceKey>(k => k.Provider.Value == Provider && k.Name.Value == ResName), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_AcceptBoundaryLengths_When_DescriptionIs150AndEmailIs100()
    {
        var resource = ExistingResource();
        var email = new string('a', 100 - "@example.com".Length) + "@example.com";

        var result = await InvokeAsync(Request(description: new string('d', 150), email: email), Repo(resource));

        Assert.IsType<NoContent>(result);
        Assert.Equal(new string('d', 150), resource.Description);
        Assert.Equal(email, resource.PrimaryInvestigatorEmail!.Value.Value);
    }

    [Fact]
    public async Task Should_ClearDescription_When_DescriptionNull()
    {
        var resource = ExistingResource();

        var result = await InvokeAsync(Request(description: null), Repo(resource));

        Assert.IsType<NoContent>(result);
        Assert.Null(resource.Description);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_ProviderNameBlank()
    {
        var repo = Repo(ExistingResource());

        var result = await InvokeAsync(Request(), repo, providerName: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName);
        repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr")]
    public async Task Should_ReturnValidationAndNotLoad_When_ResourceNameInvalid(string resourceName)
    {
        var repo = Repo(ExistingResource());

        var result = await InvokeAsync(Request(), repo, resourceName: resourceName);

        AssertProblem(result, StatusCodes.Status400BadRequest, "resource.invalid_name");
        Assert.Equal("Resource name is invalid.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_DescriptionOver150Characters()
    {
        var repo = Repo(ExistingResource());

        var result = await InvokeAsync(Request(description: new string('d', 151)), repo);

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 150 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa@example.com")]
    [InlineData("  pi@example.com                                                                                     ")]
    public async Task Should_ReturnValidationAndNotLoad_When_PrimaryInvestigatorEmailInvalidOrOver100Characters(string email)
    {
        var repo = Repo(ExistingResource());

        var result = await InvokeAsync(Request(email: email), repo);

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("A valid primary investigator email (max 100 characters) is required.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnValidation_When_EndDateEqualsStartDate()
    {
        var result = await InvokeAsync(Request(start: Start, end: Start), Repo(ExistingResource()));

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("EndDate must be after StartDate.", ((ProblemHttpResult)result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReportProviderNameBeforeResourceName_When_BothInvalid()
    {
        var result = await InvokeAsync(Request(), Repo(ExistingResource()), providerName: " ", resourceName: " ");

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName);
    }

    [Fact]
    public async Task Should_ReportDescriptionBeforeEmailAndDates_When_AllInvalid()
    {
        var result = await InvokeAsync(Request(description: new string('d', 151), email: "bad", start: End, end: Start), Repo(ExistingResource()));

        Assert.Equal("Description must not exceed 150 characters.", Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReportEmailBeforeDates_When_BothInvalid()
    {
        var result = await InvokeAsync(Request(email: "bad", start: End, end: Start), Repo(ExistingResource()));

        Assert.Equal("A valid primary investigator email (max 100 characters) is required.", Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(" ", ResName, ProviderErrorCodes.InvalidName)]
    [InlineData(Provider, " ", "resource.invalid_name")]
    public async Task Should_ValidateBeforeAuthorizing_When_ForeignCallerSendsInvalidNames(string providerName, string resourceName, string code)
    {
        var result = await InvokeAsync(Request(), Repo(ExistingResource()), ForeignCaller(), providerName, resourceName);

        AssertProblem(result, StatusCodes.Status400BadRequest, code);
    }

    [Fact]
    public async Task Should_ValidateBodyBeforeAuthorizing_When_ForeignCallerSendsBadDates()
    {
        var result = await InvokeAsync(Request(start: End, end: Start), Repo(ExistingResource()), ForeignCaller());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
    }

    [Fact]
    public async Task Should_AuthorizeOwnerOnTrimmedProviderName_When_RouteNamePadded()
    {
        var repo = Repo(ExistingResource());

        var result = await InvokeAsync(Request(), repo, providerName: "  AcMe  ");

        Assert.IsType<NoContent>(result);
        repo.Verify(r => r.FindAsync(It.Is<ProjectResourceKey>(k => k.Provider.Value == "AcMe"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
