using System.Text.Json;
using System.Text.Json.Serialization;

using Core.Domain.Shared;
using Core.Features.Enums.GetAllocationUnitType;
using Core.Features.Enums.GetLocationTypes;
using Core.Features.Enums.GetOperationModes;
using Core.Features.Enums.GetRequestStates;
using Core.Features.Enums.GetRequestTypes;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.UnitTests.Enums;

public sealed class EnumEndpointsTests
{
    private static DefaultHttpContext HttpContext(string path)
    {
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("api.test");
        http.Request.Path = path;
        return http;
    }

    [Fact]
    public void Should_ReturnEveryDomainLocationTypeByName_ExceptUnspecified()
    {
        var http = HttpContext("/api/v2/LocationTypes");

        var ok = Assert.IsType<Ok<IReadOnlyList<GetLocationTypesEndpoint.Response>>>(GetLocationTypesEndpoint.HandleAsync(http));

        Assert.Equal(
            Enum.GetValues<ELocationType>().Where(t => t != ELocationType.Unspecified),
            ok.Value!.Select(t => t.Name));
        Assert.DoesNotContain(ok.Value!, t => t.Name == ELocationType.Unspecified);
        // Bounded reference list: one complete page, no further pages.
        Assert.Equal("7", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    [Fact]
    public void Should_ReturnAllOperationModesByNameOnly_IncludingEditable()
    {
        var http = HttpContext("/api/v2/OperationModes");

        var ok = Assert.IsType<Ok<IReadOnlyList<GetOperationModesEndpoint.Response>>>(GetOperationModesEndpoint.HandleAsync(http));

        Assert.Equal(
            JsonSerializer.Serialize(new[] { new { Name = "Unspecified" }, new { Name = "ReadOnly" }, new { Name = "Editable" } }),
            JsonSerializer.Serialize(ok.Value, WireJson));
        Assert.Equal("3", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    [Fact]
    public void Should_ReturnRequestStatesByNameOnly_InFixedOrder_AsCompleteEnvelope()
    {
        var http = HttpContext("/api/v2/RequestStates");

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestStatesEndpoint.Response>>>(GetRequestStatesEndpoint.HandleAsync(http));

        Assert.Equal("5", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
        Assert.Equal(
            JsonSerializer.Serialize(new[]
            {
                new { Name = "Pending" }, new { Name = "PendingForPIAccept" }, new { Name = "PendingForHPCProject" },
                new { Name = "Accepted" }, new { Name = "Rejected" },
            }),
            JsonSerializer.Serialize(ok.Value, WireJson));
    }

    [Fact]
    public void Should_ReturnRequestTypesByNameOnly_InFixedOrder_AsCompleteEnvelope()
    {
        var http = HttpContext("/api/v2/RequestTypes");

        var ok = Assert.IsType<Ok<IReadOnlyList<GetRequestTypesEndpoint.Response>>>(GetRequestTypesEndpoint.HandleAsync(http));

        Assert.Equal("4", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
        Assert.Equal(
            JsonSerializer.Serialize(new[]
            {
                new { Name = "Default" }, new { Name = "SelfOnboarding" }, new { Name = "ProjectResourceAssignment" },
                new { Name = "HEAppECreation" },
            }),
            JsonSerializer.Serialize(ok.Value, WireJson));
    }

    [Fact]
    public void Should_ReturnEveryAllocationUnitTypeByNameOnly_ExceptNone()
    {
        var http = HttpContext("/api/v2/AllocationUnitTypes");

        var ok = Assert.IsType<Ok<IReadOnlyList<GetAllocationUnitTypesEndpoint.Response>>>(GetAllocationUnitTypesEndpoint.HandleAsync(http));

        var expected = Enum.GetValues<EAllocationUnitType>()
            .Where(t => t != EAllocationUnitType.None)
            .Select(t => new { Name = t.ToString() });
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(ok.Value, WireJson));
        Assert.Equal("6", http.Response.Headers["X-Total-Count"]);
        Assert.False(http.Response.Headers.ContainsKey("Link"));
    }

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };
}
