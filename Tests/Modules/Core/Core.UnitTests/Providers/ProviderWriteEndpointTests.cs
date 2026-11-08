using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.AddLocation;
using Core.Features.Provider.CreateProvider;
using Core.Features.Provider.DeactivateProvider;
using Core.Features.Provider.ReactivateProvider;
using Core.Features.Provider.RemoveLocation;
using Core.Features.Provider.UpdateLocation;
using Core.Features.Provider.UpdateProvider;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Contract tests for the Provider/Location write endpoints, asserting the v2 standardized error shapes
/// (ADR-0020): every failure carries a machine-readable <c>code</c> in the ProblemDetails extensions, and
/// 403/404/409 are emitted as ProblemDetails rather than empty bodies.
/// </summary>
public sealed class ProviderWriteEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";

    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    // why: owns a DIFFERENT provider rather than none, so a 403 can only come from the ownership comparison - the whole gate for a non-Admin role since the identity kind left it (ADR-0105).
    private static Caller UnauthorizedCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    // Owns "acme"; the route uses a differently-cased, whitespace-padded spelling to prove the raw-string
    // authorization keeps ADR-0012 (trim + OrdinalIgnoreCase) equality through the reorder.
    private static Caller OwnerCaller() => new(
        new IdpId("svc"), "ProviderService", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    // why: interactive on purpose - it owns "acme" and is allowed every write to it, because the identity kind no longer enters the write gate (ADR-0105).
    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("pa"), "ProviderAdmin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: false);

    private static Caller ForeignProviderAdminCaller() => new(
        new IdpId("pa"), "ProviderAdmin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: false);

    private const string PaddedProvider = "  AcMe  ";
    private const string MalformedProvider = " ";

    private static IOptions<WellKnownRoles> Roles() => Options.Create(new WellKnownRoles());

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider, ProviderAggregate? includingDeactivated = null)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        repo.Setup(r => r.FindIncludingDeactivatedAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(includingDeactivated));
        return repo;
    }

    private static Mock<ILocationQuery> LocationQuery(bool locationTypeExists = true)
    {
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.LocationTypeExistsAsync(It.IsAny<ELocationType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationTypeExists);
        return query;
    }

    private static Mock<ICountryQuery> CountryQuery(int? id = 1)
    {
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.FindIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(id);
        return query;
    }

    private static Task<IResult> InvokeCreateAsync(Caller caller, bool providerExists)
    {
        var providerQuery = new Mock<IProviderQuery>();
        providerQuery.Setup(q => q.ProviderExistsAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(providerExists);

        var request = new CreateProviderEndpoint.Request("acme", "CZ", Description: null, SupportEmail: null, IsPrivate: false);

        return CreateProviderEndpoint.HandleAsync(
            request, caller, Repo(provider: null).Object, providerQuery.Object, CountryQuery().Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<CreateProviderEndpoint.AuditCategory>(), NullLogger<CreateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnForbiddenWithCode_When_CreateByNonAdmin()
    {
        var result = await InvokeCreateAsync(UnauthorizedCaller(), providerExists: false);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Only an administrator may create a provider.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnConflictWithCode_When_CreateNameExists()
    {
        var result = await InvokeCreateAsync(AdminCaller(), providerExists: true);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.NameAlreadyExists, problem.ProblemDetails.Extensions["code"]);
    }

    private static Task<IResult> InvokeUpdateAsync(Caller caller, ProviderAggregate? provider)
    {
        var request = new UpdateProviderEndpoint.Request("CZ", Description: null, SupportEmail: null, IsPrivate: false);

        return UpdateProviderEndpoint.HandleAsync(
            Provider, request, caller, Repo(provider).Object, CountryQuery().Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(), NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnForbiddenWithCode_When_UpdateNotAuthorized()
    {
        var result = await InvokeUpdateAsync(UnauthorizedCaller(), ProviderAggregate.Create(ProviderName.From(Provider), 1));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_UpdateProviderMissing()
    {
        var result = await InvokeUpdateAsync(InteractiveProviderAdminCaller(), provider: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnForbiddenWithCode_When_UpdateByProviderAdminOfAnotherProvider()
    {
        var result = await InvokeUpdateAsync(ForeignProviderAdminCaller(), ProviderAggregate.Create(ProviderName.From(Provider), 1));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_DeactivateProviderMissing()
    {
        var result = await DeactivateProviderEndpoint.HandleAsync(
            Provider, InteractiveProviderAdminCaller(), Repo(provider: null, includingDeactivated: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<DeactivateProviderEndpoint.AuditCategory>(), NullLogger<DeactivateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnForbiddenWithCode_When_ReactivateNotAuthorized()
    {
        var result = await ReactivateProviderEndpoint.HandleAsync(
            Provider, UnauthorizedCaller(), Repo(provider: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<ReactivateProviderEndpoint.AuditCategory>(), NullLogger<ReactivateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnForbiddenWithCode_When_AddLocationNotAuthorized()
    {
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null);

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, UnauthorizedCaller(), Repo(provider: null).Object,
            LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnCreatedWithNaturalKeyLocation_When_AddLocationValid()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null);

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider).Object,
            LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var created = Assert.IsType<Created<AddLocationEndpoint.Response>>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        // Location addresses the new location by its natural key — no surrogate GUID, and set at this point
        // (explicit URL) so a dead route name cannot pass here only to 500 at response execution (ADR-0024).
        Assert.Equal($"v2/Providers/{Provider}/Locations/gpu", created.Location);
        Assert.Equal(Provider, created.Value!.ProviderName);
        Assert.Equal("gpu", created.Value.Name);
    }

    [Fact]
    public async Task Should_StoreRequestedLocationType_When_AddLocationValid()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.Kubernetes, IsPrivate: false, IsShared: true, Description: null);
        var locationQuery = LocationQuery();
        var unitOfWork = SavingUnitOfWork();

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider).Object,
            locationQuery.Object, Roles(), unitOfWork.Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        Assert.IsType<Created<AddLocationEndpoint.Response>>(result);
        Assert.Equal(ELocationType.Kubernetes, provider.Locations.Single().LocationType);
        locationQuery.Verify(q => q.LocationTypeExistsAsync(ELocationType.Kubernetes, It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ELocationType.Unspecified)]
    [InlineData((ELocationType)999)]
    public async Task Should_ReturnValidationWithInvalidLocationTypeCode_When_AddLocationTypeNotDefined(ELocationType locationType)
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", locationType, IsPrivate: false, IsShared: true, Description: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider).Object,
            LocationQuery().Object, Roles(), unitOfWork.Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidLocationType, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(provider.Locations);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidLocationTypeCode_When_AddLocationTypeHasNoRow()
    {
        var provider = ProviderAggregate.Create(ProviderName.From(Provider), countryId: 1);
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.Quantum, IsPrivate: false, IsShared: true, Description: null);
        var unitOfWork = SavingUnitOfWork();

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider).Object,
            LocationQuery(locationTypeExists: false).Object, Roles(), unitOfWork.Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidLocationType, problem.ProblemDetails.Extensions["code"]);
        Assert.Empty(provider.Locations);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_AddLocationProviderMissingAndTypeUnspecified()
    {
        var request = new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.Unspecified, IsPrivate: false, IsShared: true, Description: null);

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider: null).Object,
            LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_UpdateLocationProviderMissing()
    {
        var request = new UpdateLocationEndpoint.Request("https://gpu", Description: null);

        var result = await UpdateLocationEndpoint.HandleAsync(
            Provider, "gpu", request, InteractiveProviderAdminCaller(), Repo(provider: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateLocationEndpoint.AuditCategory>(), NullLogger<UpdateLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFoundWithCode_When_RemoveLocationProviderMissing()
    {
        var result = await RemoveLocationEndpoint.HandleAsync(
            Provider, "gpu", InteractiveProviderAdminCaller(), Repo(provider: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<RemoveLocationEndpoint.AuditCategory>(), NullLogger<RemoveLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    // ── Validation (400) shape ───────────────────────────────────────────────
    // ADR-0020 amendment: a single invalid input is a ProblemHttpResult (400) whose detail is the human
    // message and whose extensions["code"] is the specific machine code — not a ValidationProblem errors dict.

    private static Task<IResult> InvokeCreateWithRequestAsync(CreateProviderEndpoint.Request request, int? countryId = 1)
    {
        var providerQuery = new Mock<IProviderQuery>();
        providerQuery.Setup(q => q.ProviderExistsAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return CreateProviderEndpoint.HandleAsync(
            request, AdminCaller(), Repo(provider: null).Object, providerQuery.Object, CountryQuery(countryId).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<CreateProviderEndpoint.AuditCategory>(), NullLogger<CreateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidNameCode_When_CreateProviderNameEmpty()
    {
        var request = new CreateProviderEndpoint.Request(" ", "CZ", Description: null, SupportEmail: null, IsPrivate: false);

        var result = await InvokeCreateWithRequestAsync(request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidName, problem.ProblemDetails.Extensions["code"]);
        Assert.False(string.IsNullOrWhiteSpace(problem.ProblemDetails.Detail));
    }

    [Fact]
    public async Task Should_ReturnValidationWithRequiredCode_When_CreateProviderCountryCodeMissing()
    {
        var request = new CreateProviderEndpoint.Request("acme", " ", Description: null, SupportEmail: null, IsPrivate: false);

        var result = await InvokeCreateWithRequestAsync(request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.Required, problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Country code is required.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidFormatCode_When_CreateProviderDescriptionTooLong()
    {
        var request = new CreateProviderEndpoint.Request(
            "acme", "CZ", Description: new string('x', 501), SupportEmail: null, IsPrivate: false);

        var result = await InvokeCreateWithRequestAsync(request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.InvalidFormat, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidFormatCode_When_CreateProviderSupportEmailInvalid()
    {
        var request = new CreateProviderEndpoint.Request(
            "acme", "CZ", Description: null, SupportEmail: "not-an-email", IsPrivate: false);

        var result = await InvokeCreateWithRequestAsync(request);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.InvalidFormat, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithCountryNotFoundCode_When_CreateProviderCountryUnknown()
    {
        var request = new CreateProviderEndpoint.Request("acme", "ZZ", Description: null, SupportEmail: null, IsPrivate: false);

        var result = await InvokeCreateWithRequestAsync(request, countryId: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.CountryNotFound, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithRequiredCode_When_UpdateProviderCountryCodeMissing()
    {
        var request = new UpdateProviderEndpoint.Request(" ", Description: null, SupportEmail: null, IsPrivate: false);

        var result = await UpdateProviderEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(ProviderAggregate.Create(ProviderName.From(Provider), 1)).Object,
            CountryQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(), NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(CommonErrorCodes.Required, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidNameCode_When_DeactivateProviderNameEmpty()
    {
        var result = await DeactivateProviderEndpoint.HandleAsync(
            " ", AdminCaller(), Repo(provider: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<DeactivateProviderEndpoint.AuditCategory>(), NullLogger<DeactivateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidName, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidEndpointCode_When_AddLocationEndpointMissing()
    {
        var request = new AddLocationEndpoint.Request("gpu", " ", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null);

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider: null).Object,
            LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidEndpoint, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidLocationNameCode_When_AddLocationNameEmpty()
    {
        var request = new AddLocationEndpoint.Request(" ", "https://gpu", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null);

        var result = await AddLocationEndpoint.HandleAsync(
            Provider, request, InteractiveProviderAdminCaller(), Repo(provider: null).Object,
            LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidLocationName, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnValidationWithInvalidLocationNameCode_When_RemoveLocationNameEmpty()
    {
        var result = await RemoveLocationEndpoint.HandleAsync(
            Provider, " ", InteractiveProviderAdminCaller(), Repo(ProviderAggregate.Create(ProviderName.From(Provider), 1)).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<RemoveLocationEndpoint.AuditCategory>(), NullLogger<RemoveLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidLocationName, problem.ProblemDetails.Extensions["code"]);
    }

    // ── Authorize-before-validate ordering (#261b) ───────────────────────────
    // Each endpoint authorizes the raw provider name before parsing it, so a malformed name never reveals — via
    // a 400 — that it reached validation: a non-admin sees 403, an authorized caller still sees the 400. Owner
    // equality survives the raw-string path (ADR-0012), and admin still validates once authorization passes.

    private static Task<IResult> InvokeDeactivateAsync(Caller caller, string providerName) =>
        DeactivateProviderEndpoint.HandleAsync(
            providerName, caller, Repo(provider: null, includingDeactivated: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<DeactivateProviderEndpoint.AuditCategory>(), NullLogger<DeactivateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeReactivateAsync(Caller caller, string providerName) =>
        ReactivateProviderEndpoint.HandleAsync(
            providerName, caller, Repo(provider: null, includingDeactivated: null).Object,
            Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<ReactivateProviderEndpoint.AuditCategory>(), NullLogger<ReactivateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeAddLocationAsync(Caller caller, string providerName) =>
        AddLocationEndpoint.HandleAsync(
            providerName, new AddLocationEndpoint.Request("gpu", "https://gpu", ELocationType.HPC, IsPrivate: false, IsShared: true, Description: null),
            caller, Repo(provider: null).Object, LocationQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<AddLocationEndpoint.AuditCategory>(), NullLogger<AddLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeUpdateProviderAsync(Caller caller, string providerName) =>
        UpdateProviderEndpoint.HandleAsync(
            providerName, new UpdateProviderEndpoint.Request("CZ", Description: null, SupportEmail: null, IsPrivate: false),
            caller, Repo(provider: null).Object, CountryQuery().Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(), NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeRemoveLocationAsync(Caller caller, string providerName) =>
        RemoveLocationEndpoint.HandleAsync(
            providerName, "gpu", caller, Repo(provider: null).Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<RemoveLocationEndpoint.AuditCategory>(), NullLogger<RemoveLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> InvokeUpdateLocationAsync(Caller caller, string providerName) =>
        UpdateLocationEndpoint.HandleAsync(
            providerName, "gpu", new UpdateLocationEndpoint.Request("https://gpu", Description: null),
            caller, Repo(provider: null).Object, Roles(), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateLocationEndpoint.AuditCategory>(), NullLogger<UpdateLocationEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static Task<IResult> Dispatch(string endpoint, Caller caller, string providerName) => endpoint switch
    {
        "Deactivate" => InvokeDeactivateAsync(caller, providerName),
        "Reactivate" => InvokeReactivateAsync(caller, providerName),
        "AddLocation" => InvokeAddLocationAsync(caller, providerName),
        "UpdateProvider" => InvokeUpdateProviderAsync(caller, providerName),
        "RemoveLocation" => InvokeRemoveLocationAsync(caller, providerName),
        "UpdateLocation" => InvokeUpdateLocationAsync(caller, providerName),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null),
    };

    public static TheoryData<string> ReorderedEndpoints() => new()
    {
        "Deactivate", "Reactivate", "AddLocation", "UpdateProvider", "RemoveLocation", "UpdateLocation",
    };

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_Return403NotBadRequest_When_NonAdminSendsMalformedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, UnauthorizedCaller(), MalformedProvider);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
    }

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_ReturnBadRequest_When_AdminSendsMalformedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, AdminCaller(), MalformedProvider);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidName, problem.ProblemDetails.Extensions["code"]);
    }

    [Theory]
    [MemberData(nameof(ReorderedEndpoints))]
    public async Task Should_Authorize_When_OwnerSendsPaddedProviderName(string endpoint)
    {
        var result = await Dispatch(endpoint, OwnerCaller(), PaddedProvider);

        // Authorization and validation both pass on the padded/differently-cased owner name, so the flow reaches
        // the (absent) provider and returns 404 — never 403 (auth intact) or 400 (parsed after auth).
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("provider.not_found", problem.ProblemDetails.Extensions["code"]);
    }
}
