using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
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
/// Characterization tests for <see cref="UpdateProviderEndpoint.HandleAsync"/> (T-197-04): they pin every
/// branch of the handler as it stands before the extract-method refactor, and the refactor does not edit them.
/// </summary>
public sealed class UpdateProviderEndpointCharacterizationTests
{
    private const string Provider = "acme";

    private static Caller OwnerCaller() => new(
        new IdpId("pa"), "ProviderAdmin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: false);

    private static ProviderAggregate ExistingProvider(EOperationMode mode = EOperationMode.Editable) =>
        ProviderAggregate.Create(
            ProviderName.From(Provider), countryId: 1, Description.Create("old"), Email.Create("old@example.com"),
            mode, isPrivate: false);

    private static Mock<IProviderRepository> Repo(ProviderAggregate? provider)
    {
        var repo = new Mock<IProviderRepository>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(provider));
        return repo;
    }

    private static Mock<ICountryQuery> CountryQuery(int? id)
    {
        var query = new Mock<ICountryQuery>();
        query.Setup(q => q.FindIdByCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(id);
        return query;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static UpdateProviderEndpoint.Request ValidRequest(
        string? description = "new description", string? supportEmail = "support@example.com") =>
        new("DE", description, supportEmail, IsPrivate: true);

    private static Task<IResult> InvokeAsync(
        UpdateProviderEndpoint.Request request,
        Mock<IProviderRepository> repo,
        Mock<ICountryQuery> countryQuery,
        Mock<IUnitOfWork> unitOfWork) =>
        UpdateProviderEndpoint.HandleAsync(
            Provider, request, OwnerCaller(), repo.Object, countryQuery.Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(), NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNoContentAndApplyEveryField_When_RequestValid()
    {
        var provider = ExistingProvider();
        var countryQuery = CountryQuery(id: 7);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ValidRequest(), Repo(provider), countryQuery, unitOfWork);

        Assert.IsType<NoContent>(result);
        Assert.Equal("new description", provider.Description!.Value.Value);
        Assert.Equal("support@example.com", provider.SupportEmail!.Value.Value);
        Assert.True(provider.IsPrivate);
        Assert.Equal(7, provider.CountryId);
        countryQuery.Verify(q => q.FindIdByCodeAsync("DE", It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", "   ")]
    public async Task Should_ClearDescriptionAndSupportEmail_When_TheyAreBlank(string? description, string? supportEmail)
    {
        var provider = ExistingProvider();

        var result = await InvokeAsync(
            ValidRequest(description, supportEmail), Repo(provider), CountryQuery(id: 1), SavingUnitOfWork());

        Assert.IsType<NoContent>(result);
        Assert.Null(provider.Description);
        Assert.Null(provider.SupportEmail);
    }

    [Fact]
    public async Task Should_AcceptDescription_When_ExactlyFiveHundredCharacters()
    {
        var provider = ExistingProvider();
        var description = new string('x', 500);

        var result = await InvokeAsync(ValidRequest(description), Repo(provider), CountryQuery(id: 1), SavingUnitOfWork());

        Assert.IsType<NoContent>(result);
        Assert.Equal(description, provider.Description!.Value.Value);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_DescriptionOverFiveHundredCharacters()
    {
        var repo = Repo(ExistingProvider());
        var countryQuery = CountryQuery(id: 1);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ValidRequest(new string('x', 501)), repo, countryQuery, unitOfWork);

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 500 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        countryQuery.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_SupportEmailInvalid()
    {
        var repo = Repo(ExistingProvider());
        var countryQuery = CountryQuery(id: 1);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ValidRequest(supportEmail: "not-an-email"), repo, countryQuery, unitOfWork);

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Invalid email address.", ((ProblemHttpResult)result).ProblemDetails.Detail);
        countryQuery.VerifyNoOtherCalls();
        repo.VerifyNoOtherCalls();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReportDescriptionBeforeSupportEmail_When_BothInvalid()
    {
        var result = await InvokeAsync(
            ValidRequest(new string('x', 501), "not-an-email"), Repo(ExistingProvider()), CountryQuery(id: 1), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.InvalidFormat);
        Assert.Equal("Description must not exceed 500 characters.", ((ProblemHttpResult)result).ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_ReportMissingCountryCodeBeforeDescription_When_BothInvalid()
    {
        var request = new UpdateProviderEndpoint.Request(" ", new string('x', 501), "not-an-email", IsPrivate: false);

        var result = await InvokeAsync(request, Repo(ExistingProvider()), CountryQuery(id: 1), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, CommonErrorCodes.Required);
    }

    [Fact]
    public async Task Should_ReturnValidationAndNotLoad_When_CountryUnknown()
    {
        var repo = Repo(ExistingProvider());
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ValidRequest(), repo, CountryQuery(id: null), unitOfWork);

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.CountryNotFound);
        repo.VerifyNoOtherCalls();
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReportUnknownCountryBeforeMissingProvider_When_Both()
    {
        var result = await InvokeAsync(ValidRequest(), Repo(provider: null), CountryQuery(id: null), SavingUnitOfWork());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.CountryNotFound);
    }

    [Fact]
    public async Task Should_ReturnConflictAndLeaveProviderUnchanged_When_ProviderReadOnly()
    {
        var provider = ExistingProvider(EOperationMode.ReadOnly);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ValidRequest(), Repo(provider), CountryQuery(id: 7), unitOfWork);

        AssertProblem(result, StatusCodes.Status409Conflict, ProviderErrorCodes.ProviderReadOnly);
        Assert.Equal("old", provider.Description!.Value.Value);
        Assert.Equal("old@example.com", provider.SupportEmail!.Value.Value);
        Assert.False(provider.IsPrivate);
        Assert.Equal(1, provider.CountryId);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_LoadProviderByTrimmedName_When_RouteNamePadded()
    {
        var repo = Repo(ExistingProvider());

        await UpdateProviderEndpoint.HandleAsync(
            "  AcMe  ", ValidRequest(), OwnerCaller(), repo.Object, CountryQuery(id: 1).Object,
            Options.Create(new WellKnownRoles()), SavingUnitOfWork().Object, new RecordingAuditLogger<UpdateProviderEndpoint.AuditCategory>(), NullLogger<UpdateProviderEndpoint.AuditCategory>.Instance, CancellationToken.None);

        repo.Verify(r => r.FindAsync(It.Is<ProviderName>(n => n.Value == "AcMe"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
