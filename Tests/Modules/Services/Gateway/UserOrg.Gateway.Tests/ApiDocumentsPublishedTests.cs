using UserOrg.Shared.OpenApi;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The published document set follows the <c>V1Api</c> flag: the legacy pair only while v1 is on, the
/// <see cref="ApiDocuments.Service"/> document of the ungated legacy endpoints and the v2 documents always.
/// </summary>
public sealed class ApiDocumentsPublishedTests
{
    [Fact]
    public void Should_ListServiceAndTheV2DocumentsOnly_When_V1ApiIsOff()
    {
        Assert.Equal(
            [ApiDocuments.Service, ApiDocuments.V2Admin, ApiDocuments.V2ProviderAdmin, ApiDocuments.V2User],
            ApiDocuments.Published(isV1ApiEnabled: false).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Should_AddTheLegacyPair_When_V1ApiIsOn()
    {
        Assert.Equal(
            [ApiDocuments.Administration, ApiDocuments.Service, ApiDocuments.User, ApiDocuments.V2Admin, ApiDocuments.V2ProviderAdmin, ApiDocuments.V2User],
            ApiDocuments.Published(isV1ApiEnabled: true).Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Should_NameTheServiceDocumentService_When_ItIsServedAtApiServiceOpenApiJson()
    {
        Assert.Equal("Service", ApiDocuments.Service);
    }
}