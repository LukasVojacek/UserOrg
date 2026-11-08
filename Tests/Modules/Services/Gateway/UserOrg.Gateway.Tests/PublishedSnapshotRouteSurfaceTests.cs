using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Features.RouteExistence;
using UserOrg.Shared.OpenApi;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The oracle as a gateway with <c>V1Api</c> off builds it: from the committed snapshots of
/// <see cref="ApiDocuments.Published"/>(false) alone. The v1 routes go <see cref="ERouteExistence.NotServed"/>,
/// and the ungated legacy endpoints stay <see cref="ERouteExistence.Served"/> because the Service document
/// describes them, not the legacy pair.
/// </summary>
public sealed class PublishedSnapshotRouteSurfaceTests
{
    [Theory]
    [InlineData("/api/Users")]
    [InlineData("/api/UserInfo")]
    [InlineData("/api/Enums/RequestStates")]
    [InlineData("/api/Service/SyncDetails")]
    [InlineData("/api/Administration/User")]
    public void Should_AnswerNotServed_When_TheRouteIsAV1Route(string path)
    {
        Assert.Equal(ERouteExistence.NotServed, RouteSurfaceFixture.PublishedV1OffSurface().Evaluate(new PathString(path)));
    }

    [Theory]
    [InlineData("/api/remi/users")]
    [InlineData("/api/remi/projects")]
    [InlineData("/api/version")]
    [InlineData("/api/DataStaging/WorkerData")]
    [InlineData("/api/DataStaging/DataStagingLocations")]
    [InlineData("/api/Heappe/Partitions/PRJ/resource-1")]
    [InlineData("/api/Heappe/KillSwitch")]
    [InlineData("/api/Heappe/KillSwitch/Provider/Location")]
    [InlineData("/api/Heappe/ResourceAudit/run")]
    [InlineData("/api/Heappe/CommandTemplate/PRJ/resource-1")]
    [InlineData("/api/v2/Service/ProjectsInfo")]
    public void Should_AnswerServed_When_TheRouteIsUngatedOrV2(string path)
    {
        Assert.Equal(ERouteExistence.Served, RouteSurfaceFixture.PublishedV1OffSurface().Evaluate(new PathString(path)));
    }

    /// <summary>
    /// A hidden legacy document URL is documentation surface, never judged, so it keeps the auth stage's
    /// answer (401 anonymous, 403 <c>auth.no_permission_mapping</c> authenticated) instead of a 404.
    /// </summary>
    [Theory]
    [InlineData("/api/User/openapi.json")]
    [InlineData("/api/Administration/openapi.json")]
    public void Should_LeaveTheHiddenDocumentUrlUnjudged_When_V1ApiIsOff(string path)
    {
        Assert.Equal(ERouteExistence.Unknown, RouteSurfaceFixture.PublishedV1OffSurface().Evaluate(new PathString(path)));
    }
}