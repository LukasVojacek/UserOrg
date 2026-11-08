using System.Reflection;

using Core.Composition;

using Microsoft.AspNetCore.Mvc.ApiExplorer;

using UserOrganizationManagement.API.Composition;

namespace UserOrganizationManagement.Application.Tests;

public sealed class V2CoreExtensionsIsV2Tests
{
    [Theory]
    [InlineData("api/v2/Projects", true)]
    [InlineData("API/V2/Projects", true)]
    [InlineData("api/v2/Public/Locations", true)]
    [InlineData("api/v2", false)]
    [InlineData("api/v2x/Projects", false)]
    [InlineData("api/Projects", false)]
    [InlineData("v2/api/Projects", false)]
    [InlineData(null, false)]
    public void Should_MatchOnlyTheV2PrefixWithItsTrailingSlash(string? relativePath, bool expected)
    {
        Assert.Equal(expected, V2CoreExtensions.IsV2(new ApiDescription { RelativePath = relativePath }));
    }

    [Fact]
    public void Should_ExposeTheV2RoutePrefixAsAPublicConstant_On_TheCoreEndpointsExtensions()
    {
        var field = typeof(EndpointsExtensions).GetField("V2RoutePrefix", BindingFlags.Public | BindingFlags.Static);

        Assert.True(field is { IsLiteral: true }, "Core.Composition.EndpointsExtensions declares no public const V2RoutePrefix");
        Assert.Equal("api/v2", field.GetRawConstantValue());
    }
}
