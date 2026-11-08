using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Boots the real <c>Program</c> once per <see cref="FlagCombination"/> and pins what each host serves: the
/// legacy groups follow <c>V1Api</c>, Core v2 follows <c>V2Api</c>, and v1 <c>api/DataStaging</c>,
/// <c>api/version</c> and the DDI v2 routes are mapped whatever the flags say. The fourth combination,
/// <c>V1Api</c> on with <c>V2Api</c> off, is <see cref="V2ApiFlagOffBootTests"/>.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed partial class FeatureFlagCombinationBootTests
{
    private static readonly Dictionary<FlagCombination, Lazy<ProgramBootFactory>> _factories = new()
    {
        [FlagCombination.V2Only] = new(() => new ProgramBootFactory(isV1ApiEnabled: false, isV2ApiEnabled: true)),
        [FlagCombination.Neither] = new(() => new ProgramBootFactory(isV1ApiEnabled: false, isV2ApiEnabled: false)),
        [FlagCombination.Both] = new(() => new ProgramBootFactory(isV1ApiEnabled: true, isV2ApiEnabled: true)),
    };

    public enum FlagCombination
    {
        V2Only,
        Neither,
        Both,
    }

    [Theory]
    [InlineData(FlagCombination.V2Only, "api/Enums/LocationTypes", false)]
    [InlineData(FlagCombination.V2Only, "api/Service/SyncDetails", false)]
    [InlineData(FlagCombination.V2Only, "api/DataStaging/WorkerData", true)]
    [InlineData(FlagCombination.V2Only, "api/version", true)]
    [InlineData(FlagCombination.V2Only, "api/v2/Projects", true)]
    [InlineData(FlagCombination.V2Only, "api/v2/DataStaging/WorkerData", true)]
    [InlineData(FlagCombination.V2Only, "api/remi/users", true)]
    [InlineData(FlagCombination.V2Only, "api/Heappe/KillSwitch", true)]
    [InlineData(FlagCombination.V2Only, "api/Heappe/ResourceAudit/run", true)]
    [InlineData(FlagCombination.Neither, "api/Enums/LocationTypes", false)]
    [InlineData(FlagCombination.Neither, "api/Service/SyncDetails", false)]
    [InlineData(FlagCombination.Neither, "api/DataStaging/WorkerData", true)]
    [InlineData(FlagCombination.Neither, "api/version", true)]
    [InlineData(FlagCombination.Neither, "api/v2/Projects", false)]
    [InlineData(FlagCombination.Neither, "api/v2/DataStaging/WorkerData", true)]
    [InlineData(FlagCombination.Both, "api/Enums/LocationTypes", true)]
    [InlineData(FlagCombination.Both, "api/Service/SyncDetails", true)]
    [InlineData(FlagCombination.Both, "api/DataStaging/WorkerData", true)]
    [InlineData(FlagCombination.Both, "api/version", true)]
    [InlineData(FlagCombination.Both, "api/v2/Projects", true)]
    [InlineData(FlagCombination.Both, "api/v2/DataStaging/WorkerData", true)]
    public void Should_MapOnlyTheEnabledSurfaces_When_BootedUnderFlagCombination(FlagCombination combination, string routeTemplate, bool expectedMapped)
    {
        var mapped = _factories[combination].Value.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => (endpoint.RoutePattern.RawText ?? string.Empty).Trim('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expectedMapped, mapped.Contains(routeTemplate));
    }

    [Theory]
    [InlineData(FlagCombination.V2Only, "/api/Enums/LocationTypes", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.V2Only, "/api/v2/Projects", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.V2Only, "/api/v2/DataStaging/WorkerData", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.V2Only, "/api/version", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Neither, "/api/Enums/LocationTypes", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.Neither, "/api/v2/Projects", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.Neither, "/api/v2/DataStaging/WorkerData", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.Neither, "/api/version", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Both, "/api/Enums/LocationTypes", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.Both, "/api/v2/Projects", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.Both, "/api/v2/DataStaging/WorkerData", HttpStatusCode.Unauthorized)]
    [InlineData(FlagCombination.Both, "/api/version", HttpStatusCode.OK)]
    public async Task Should_ReturnExpectedStatus_When_RequestingRouteUnderFlagCombination(FlagCombination combination, string path, HttpStatusCode expected)
    {
        using var client = _factories[combination].Value.CreateClient();

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(FlagCombination.V2Only, "User", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.V2Only, "Administration", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.V2Only, "Service", HttpStatusCode.OK)]
    [InlineData(FlagCombination.V2Only, "v2-User", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Neither, "User", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.Neither, "Administration", HttpStatusCode.NotFound)]
    [InlineData(FlagCombination.Neither, "Service", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Both, "User", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Both, "Administration", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Both, "Service", HttpStatusCode.OK)]
    [InlineData(FlagCombination.Both, "v2-User", HttpStatusCode.OK)]
    public async Task Should_PublishTheLegacyDocumentsOnlyWhileV1ApiIsOn_When_RequestingADocument(FlagCombination combination, string documentName, HttpStatusCode expected)
    {
        using var client = _factories[combination].Value.CreateClient();

        using var response = await client.GetAsync(new Uri($"/api/{documentName}/openapi.json", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(FlagCombination.V2Only, false)]
    [InlineData(FlagCombination.Neither, false)]
    [InlineData(FlagCombination.Both, true)]
    public async Task Should_OfferExactlyThePublishedDocumentsInScalar_When_BootedUnderFlagCombination(FlagCombination combination, bool isV1ApiEnabled)
    {
        using var client = _factories[combination].Value.CreateClient();

        var page = await client.GetStringAsync(new Uri("/api", UriKind.Relative), TestContext.Current.CancellationToken);
        var offered = ScalarSourceTitle().Matches(page).Select(match => match.Groups["title"].Value);

        Assert.Equal(
            ApiDocuments.Published(isV1ApiEnabled).Order(StringComparer.Ordinal),
            offered.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\{""title"":""(?<title>[^""]+)"",""url"":""[^""]*/openapi\.json""\}")]
    private static partial Regex ScalarSourceTitle();
}