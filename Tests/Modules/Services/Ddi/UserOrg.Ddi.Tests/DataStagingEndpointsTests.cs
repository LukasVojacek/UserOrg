using System.Security.Claims;
using System.Text;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using UserOrg.Ddi.Features.DataStaging;

using Xunit;

namespace UserOrg.Ddi.Tests;

public sealed class DataStagingEndpointsTests
{
    private static ClaimsPrincipal Principal(string? preferredUsername) =>
        new(new ClaimsIdentity(preferredUsername is null ? [] : [new Claim("preferred_username", preferredUsername)]));

    private static Error SomeError() => new("Vault.Failed", ErrorSeverity.Error, "boom");

    private static DefaultHttpContext ContextWithBody(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return context;
    }

    [Fact]
    public async Task Should_Return_Public_And_Private_When_Worker_Has_Both()
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetServiceAccessKeyDataAsync("worker1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Success("PRIV"));
        vault.Setup(v => v.GetServiceAccessKeyDataAsync("PublicData", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Success("PUB"));

        var result = await GetWorkerDataEndpoint.HandleAsync(Principal("worker1"), vault.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<GetWorkerDataEndpoint.Response>>(result);
        Assert.Equal("worker1", ok.Value!.Name);
        Assert.Equal("PUB", ok.Value.PublicData.JsonData);
        Assert.Equal("PRIV", ok.Value.PrivateData.JsonData);
    }

    [Fact]
    public async Task Should_Skip_Private_Fetch_When_DataName_Is_PublicData()
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetServiceAccessKeyDataAsync("PublicData", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Success("PUB"));

        var result = await GetWorkerDataEndpoint.HandleAsync(Principal("PublicData"), vault.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<GetWorkerDataEndpoint.Response>>(result);
        Assert.Equal(string.Empty, ok.Value!.PrivateData.JsonData);
        vault.Verify(v => v.GetServiceAccessKeyDataAsync("PublicData", It.IsAny<CancellationToken>()), Times.Once);
        vault.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_400_When_DataStaging_Claim_Missing()
    {
        var vault = new Mock<IHashiCorpVaultClient>();

        var result = await GetWorkerDataEndpoint.HandleAsync(Principal(null), vault.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        vault.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_500_When_Vault_Private_Fetch_Fails()
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetServiceAccessKeyDataAsync("worker1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Failure(SomeError()));

        var result = await GetWorkerDataEndpoint.HandleAsync(Principal("worker1"), vault.Object, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    [Fact]
    public async Task Should_Return_Keys_When_Vault_Succeeds()
    {
        IReadOnlyCollection<DataStagingLocationKey> keys = [new() { Name = "k1" }];
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetDDIDataStagingKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<DataStagingLocationKey>, Error>.Success(keys));

        var result = await GetDataStagingLocationsEndpoint.HandleAsync(vault.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<IReadOnlyCollection<DataStagingLocationKey>>>(result);
        Assert.Single(ok.Value!);
    }

    [Fact]
    public async Task Should_Return_500_When_DataStagingLocations_Vault_Fails()
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetDDIDataStagingKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<DataStagingLocationKey>, Error>.Failure(SomeError()));

        var result = await GetDataStagingLocationsEndpoint.HandleAsync(vault.Object, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    [Fact]
    public async Task Should_BulkUpdate_When_Array_Payload()
    {
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.UpdateServiceAccessKeysBulkAsync(It.IsAny<IEnumerable<DataStagingLocationKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BulkUpdateResult, Error>.Success(new BulkUpdateResult(1, 0, 0)));

        var context = ContextWithBody("""[{"Name":"a","JsonData":"x"}]""");
        var result = await UpdateDataStagingLocationsEndpoint.HandleAsync(context, vault.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<BulkUpdateResult>>(result);
        Assert.Equal(1, ok.Value!.Created);
    }

    [Fact]
    public async Task Should_Name_Entries_From_Properties_When_Object_Payload()
    {
        List<DataStagingLocationKey>? captured = null;
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.UpdateServiceAccessKeysBulkAsync(It.IsAny<IEnumerable<DataStagingLocationKey>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<DataStagingLocationKey>, CancellationToken>((items, _) => captured = [.. items])
            .ReturnsAsync(Result<BulkUpdateResult, Error>.Success(new BulkUpdateResult(0, 1, 0)));

        var context = ContextWithBody("""{"entryA":{"JsonData":"x"}}""");
        var result = await UpdateDataStagingLocationsEndpoint.HandleAsync(context, vault.Object, CancellationToken.None);

        Assert.IsType<Ok<BulkUpdateResult>>(result);
        Assert.Equal("entryA", Assert.Single(captured!).Name);
    }

    [Fact]
    public async Task Should_Return_400_When_Payload_Empty()
    {
        var vault = new Mock<IHashiCorpVaultClient>();

        var context = ContextWithBody("[]");
        var result = await UpdateDataStagingLocationsEndpoint.HandleAsync(context, vault.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        vault.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_400_When_Json_Is_Malformed()
    {
        var vault = new Mock<IHashiCorpVaultClient>();

        var context = ContextWithBody("{ not valid json");
        var result = await UpdateDataStagingLocationsEndpoint.HandleAsync(context, vault.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        vault.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_400_When_Array_Element_Is_Malformed()
    {
        var vault = new Mock<IHashiCorpVaultClient>();

        // Name is a string property; an object value makes the array deserialize throw JsonException —
        // which previously surfaced as an unhandled 500 instead of a 400.
        var context = ContextWithBody("""[{"Name":{"nested":1}}]""");
        var result = await UpdateDataStagingLocationsEndpoint.HandleAsync(context, vault.Object, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
        vault.VerifyNoOtherCalls();
    }
}
