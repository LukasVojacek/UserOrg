using System.Text.Json;

using Microsoft.AspNetCore.Http;

using UserOrg.Gateway.Features.RouteExistence;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// The route-existence oracle of ADR-0106: what it judges, what it refuses to judge, and the direction it
/// fails in. Every answer other than <see cref="ERouteExistence.NotServed"/> leaves the request exactly
/// where it is today.
/// </summary>
public sealed class RouteSurfaceTests
{
    [Theory]
    [InlineData(RouteSurfaceFixture.ServedV2Path)]
    [InlineData(RouteSurfaceFixture.ServedLegacyPath)]
    [InlineData("/api/v2/Projects/TESTPRJ/Users")]
    [InlineData("/api/v2/PROJECTS/testprj")]
    [InlineData("/api/v2/Projects/TESTPRJ/")]
    [InlineData("/api/v2/enums/request-states")]
    [InlineData("/api/V2/ENUMS/request-states")]
    public void Should_ReportServed_When_ADocumentDescribesThePath(string path)
    {
        Assert.Equal(ERouteExistence.Served, RouteSurfaceFixture.FullSurface().Evaluate(new PathString(path)));
    }

    [Theory]
    [InlineData(RouteSurfaceFixture.UnservedV2Path)]
    [InlineData(RouteSurfaceFixture.UnservedLegacyPath)]
    [InlineData("/api/v2/Projects/TESTPRJ/Users/extra-segment")]
    [InlineData("/api/v2/Projects")]
    [InlineData("/api/v2/enums/request-states/extra")]
    public void Should_ReportNotServed_When_NoDocumentDescribesThePath(string path)
    {
        Assert.Equal(ERouteExistence.NotServed, RouteSurfaceFixture.FullSurface().Evaluate(new PathString(path)));
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/.well-known/jwks.json")]
    [InlineData("/role-management")]
    [InlineData("/_framework/blazor.web.js")]
    [InlineData("/api")]
    [InlineData("/api/scalar.js")]
    [InlineData("/api/scalar.aspnetcore.js")]
    [InlineData("/api/v2-User/openapi.json")]
    [InlineData("/api/User/openapi.json")]
    public void Should_RefuseToJudge_When_ThePathIsOutsideTheDocumentedSurface(string path)
    {
        Assert.Equal(ERouteExistence.Unknown, RouteSurfaceFixture.FullSurface().Evaluate(new PathString(path)));
    }

    [Theory]
    [InlineData(RouteSurfaceFixture.ServedV2Path)]
    [InlineData(RouteSurfaceFixture.UnservedV2Path)]
    [InlineData(RouteSurfaceFixture.ServedLegacyPath)]
    [InlineData(RouteSurfaceFixture.UnservedLegacyPath)]
    public void Should_RefuseToJudge_When_NothingHasLoaded(string path)
    {
        Assert.Equal(ERouteExistence.Unknown, RouteSurface.Empty.Evaluate(new PathString(path)));
    }

    /// <summary>
    /// A legacy surface switched off by the <c>V1Api</c> feature flag leaves the legacy documents empty.
    /// A space no document described is left unjudged, so Core keeps answering the whole legacy surface,
    /// while the v2 space is judged as usual.
    /// </summary>
    [Fact]
    public void Should_LeaveTheLegacySpaceUnjudged_When_TheLegacyDocumentsDescribeNothing()
    {
        var surface = RouteSurfaceFixture.V2OnlySurface();

        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString(RouteSurfaceFixture.ServedLegacyPath)));
        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString(RouteSurfaceFixture.UnservedLegacyPath)));
        Assert.Equal(ERouteExistence.Served, surface.Evaluate(new PathString(RouteSurfaceFixture.ServedV2Path)));
        Assert.Equal(ERouteExistence.NotServed, surface.Evaluate(new PathString(RouteSurfaceFixture.UnservedV2Path)));
    }

    /// <summary>
    /// A path item carrying no operation documents no route, so it must not make its space look described.
    /// </summary>
    [Fact]
    public void Should_RefuseToJudge_When_ADocumentCarriesPathItemsWithoutOperations()
    {
        var surface = RouteSurfaceFixture.SurfaceOf(RouteSurfaceFixture.OperationlessDocument);

        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString("/api/v2/anything")));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"paths":[]}""")]
    [InlineData("""{"paths":"/api/v2/x"}""")]
    [InlineData("""{"paths":{"/api/v2/x":"get"}}""")]
    [InlineData("""{"paths":{"/health":{"get":{}},"/apix/y":{"get":{}}}}""")]
    public void Should_LeaveBothSpacesUnjudged_When_TheDocumentsDescribeNoApiOperation(string document)
    {
        var surface = RouteSurfaceFixture.SurfaceOf(document);

        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString("/api/x")));
        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString("/api/v2/x")));
    }

    [Fact]
    public void Should_JudgeTheLegacySpace_When_OnlyTheApiRootIsDescribed()
    {
        var surface = RouteSurfaceFixture.SurfaceOf("""{"paths":{"/api/":{"get":{}}}}""");

        Assert.Equal(ERouteExistence.NotServed, surface.Evaluate(new PathString("/api/x")));
        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString("/api")));
        Assert.Equal(ERouteExistence.Unknown, surface.Evaluate(new PathString("/api/v2/x")));
    }

    [Fact]
    public void Should_RefuseToJudge_When_ThePathIsEmpty()
    {
        Assert.Equal(ERouteExistence.Unknown, RouteSurfaceFixture.FullSurface().Evaluate(PathString.Empty));
    }

    [Fact]
    public void Should_Throw_When_ADocumentIsNotJson()
    {
        Assert.ThrowsAny<JsonException>(() => RouteSurfaceFixture.SurfaceOf("not json"));
    }

    [Fact]
    public void Should_Throw_When_TheDocumentsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => RouteSurface.FromOpenApiDocuments(null!));
    }
}
