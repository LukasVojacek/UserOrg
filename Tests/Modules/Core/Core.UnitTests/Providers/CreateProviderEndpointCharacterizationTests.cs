using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Provider.CreateProvider;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests.Providers;

/// <summary>
/// Characterization tests for <see cref="CreateProviderEndpoint.HandleAsync"/> (T-197-04): together with
/// <see cref="ProviderWriteEndpointTests"/> they pin every branch of the handler as it stands before the
/// extract-method refactor, and the refactor does not edit them.
/// </summary>
public sealed class CreateProviderEndpointCharacterizationTests
{
    private static Caller AdminCaller(string role = "Admin") => new(
        new IdpId("admin"), role, null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderAdminCaller() => new(
        new IdpId("pa"), "ProviderAdmin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("acme"), IsServiceKeyIdentity: false);

    private static CreateProviderEndpoint.Request Request(
        string name = "acme",
        string countryCode = "CZ",
        string? description = "about acme",
        string? supportEmail = "support@acme.example") =>
        new(name, countryCode, description, supportEmail, IsPrivate: true);

    private sealed class Harness
    {
        public Mock<IProviderRepository> Repository { get; } = new();
        public Mock<IProviderQuery> ProviderQuery { get; } = new();
        public Mock<ICountryQuery> CountryQuery { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public ProviderAggregate? Added { get; private set; }

        public Harness(int? countryId = 3, bool providerExists = false)
        {
            Repository.Setup(r => r.AddAsync(It.IsAny<ProviderAggregate>(), It.IsAny<CancellationToken>()))
                .Callback<ProviderAggregate, CancellationToken>((p, _) => Added = p)
                .Returns((ProviderAggregate p, CancellationToken _) => ValueTask.FromResult<ProviderAggregate?>(p));
            ProviderQuery.Setup(q => q.ProviderExistsAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(providerExists);
            CountryQuery.Setup(q => q.FindIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(countryId);
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Task<IResult> InvokeAsync(CreateProviderEndpoint.Request request, Caller caller) =>
            CreateProviderEndpoint.HandleAsync(
                request, caller, Repository.Object, ProviderQuery.Object, CountryQuery.Object,
                Options.Create(new WellKnownRoles()), UnitOfWork.Object, new RecordingAuditLogger<CreateProviderEndpoint.AuditCategory>(), NullLogger<CreateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnCreatedAtRouteAndPersist_When_RequestValid()
    {
        var harness = new Harness(countryId: 3);

        var result = await harness.InvokeAsync(Request(), AdminCaller());

        var created = Assert.IsType<CreatedAtRoute<CreateProviderEndpoint.Response>>(result);
        Assert.Equal("GetProviderByNameV2", created.RouteName);
        Assert.Equal("acme", created.RouteValues["providerName"]);
        Assert.Equal("acme", created.Value!.Name);
        var added = Assert.IsType<ProviderAggregate>(harness.Added);
        Assert.Equal("acme", added.Name.Value);
        Assert.Equal(3, added.CountryId);
        Assert.Equal("about acme", added.Description!.Value.Value);
        Assert.Equal("support@acme.example", added.SupportEmail!.Value.Value);
        Assert.True(added.IsPrivate);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_UseTrimmedNameInResponseAndLookups_When_NamePadded()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(name: "  Acme  "), AdminCaller());

        var created = Assert.IsType<CreatedAtRoute<CreateProviderEndpoint.Response>>(result);
        Assert.Equal("Acme", created.Value!.Name);
        Assert.Equal("Acme", created.RouteValues["providerName"]);
        harness.ProviderQuery.Verify(q => q.ProviderExistsAsync(It.Is<ProviderName>(n => n.Value == "Acme"), It.IsAny<CancellationToken>()), Times.Once);
        harness.CountryQuery.Verify(q => q.FindIdByCodeAsync("CZ", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", "  ")]
    public async Task Should_CreateWithoutDescriptionAndSupportEmail_When_TheyAreBlank(string? description, string? supportEmail)
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(description: description, supportEmail: supportEmail), AdminCaller());

        Assert.IsType<CreatedAtRoute<CreateProviderEndpoint.Response>>(result);
        Assert.Null(harness.Added!.Description);
        Assert.Null(harness.Added.SupportEmail);
    }

    [Fact]
    public async Task Should_AcceptDescription_When_ExactlyFiveHundredCharacters()
    {
        var harness = new Harness();
        var description = new string('x', 500);

        var result = await harness.InvokeAsync(Request(description: description), AdminCaller());

        Assert.IsType<CreatedAtRoute<CreateProviderEndpoint.Response>>(result);
        Assert.Equal(description, harness.Added!.Description!.Value.Value);
    }

    [Fact]
    public async Task Should_AuthorizeAdminRoleCaseInsensitively_When_RoleSpelledInLowerCase()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(), AdminCaller(role: "admin"));

        Assert.IsType<CreatedAtRoute<CreateProviderEndpoint.Response>>(result);
    }

    [Fact]
    public async Task Should_ReturnForbiddenAndTouchNothing_When_ProviderAdminCreatesWithMalformedName()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(name: " "), ProviderAdminCaller());

        AssertProblem(result, StatusCodes.Status403Forbidden, "provider.write_forbidden");
        Assert.Equal("Only an administrator may create a provider.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        harness.Repository.VerifyNoOtherCalls();
        harness.ProviderQuery.VerifyNoOtherCalls();
        harness.CountryQuery.VerifyNoOtherCalls();
        harness.UnitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportMissingCountryCodeBeforeDescriptionAndEmail_When_AllInvalid()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(countryCode: " ", description: new string('x', 501), supportEmail: "bad"), AdminCaller());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.Required);
    }

    [Fact]
    public async Task Should_ReportDescriptionBeforeSupportEmail_When_BothInvalid()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(description: new string('x', 501), supportEmail: "bad"), AdminCaller());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 500 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        harness.CountryQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotQueryCountry_When_SupportEmailInvalid()
    {
        var harness = new Harness();

        var result = await harness.InvokeAsync(Request(supportEmail: "bad"), AdminCaller());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Invalid email address.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        harness.CountryQuery.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReportUnknownCountryBeforeNameConflict_When_Both()
    {
        var harness = new Harness(countryId: null, providerExists: true);

        var result = await harness.InvokeAsync(Request(), AdminCaller());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.CountryNotFound);
        harness.ProviderQuery.VerifyNoOtherCalls();
        harness.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_NotAddOrSave_When_NameAlreadyExists()
    {
        var harness = new Harness(providerExists: true);

        var result = await harness.InvokeAsync(Request(), AdminCaller());

        AssertProblem(result, StatusCodes.Status409Conflict, ProviderErrorCodes.NameAlreadyExists);
        Assert.Equal("A provider named 'acme' already exists.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        harness.Repository.VerifyNoOtherCalls();
        harness.UnitOfWork.VerifyNoOtherCalls();
    }
}
