using System.Text.Json;
using System.Text.Json.Serialization;

using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Location.GetLocation;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Locations;

public sealed class GetLocationEndpointTests
{
    private const string Provider = "acme";
    private const string Location = "loc-1";

    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private static readonly WellKnownRoles Roles = new();

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);

    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() },
    };

    private static Caller UserCaller() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller AdminCaller() => new(
        new IdpId("a"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller ProviderCaller() => new(
        new IdpId("p"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"));

    private static Caller ServiceKeyProviderCaller() => new(
        new IdpId("k"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(), ProviderName.Create("acme"),
        IsServiceKeyIdentity: true);

    private static Caller InteractiveProviderAdminCaller() => new(
        new IdpId("h"), "ProviderAdmin", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } },
        ProviderName.Create("acme"));

    private static LocationSummary Sample() => new(
        Location, "desc", "https://hpc.example.org", false, true, ELocationType.HPC, Provider, EOperationMode.Editable, "https://svc.example.org");

    private static async Task<IResult> InvokeAsync(
        string provider, string location, LocationSummary? row, Caller? caller = null, Action<IReadOnlyList<ProjectShortName>?>? captureScope = null)
    {
        var query = new Mock<ILocationQuery>();
        query.Setup(q => q.GetLocationByProviderAndNameAsync(It.IsAny<ProviderName>(), It.IsAny<LocationName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
            .Callback<ProviderName, LocationName, IReadOnlyList<ProjectShortName>?, CancellationToken>((_, _, s, _) => captureScope?.Invoke(s))
            .ReturnsAsync(row);

        return await GetLocationEndpoint.HandleAsync(
            new GetLocationEndpoint.Request(provider, location), caller ?? AdminCaller(), query.Object, RoleOptions(), CancellationToken.None);
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapLocation_When_FoundByProviderAndName()
    {
        var result = await InvokeAsync(Provider, Location, Sample());

        var ok = Assert.IsType<Ok<GetLocationEndpoint.Response>>(result);
        Assert.Equal(Location, ok.Value!.Name);
        Assert.Equal(Provider, ok.Value.ProviderName);
        Assert.Equal(ELocationType.HPC, ok.Value.LocationType);
    }

    [Fact]
    public async Task Should_EmitModeByName_When_FoundByProviderAndName()
    {
        var result = await InvokeAsync(Provider, Location, Sample());

        var ok = Assert.IsType<Ok<GetLocationEndpoint.Response>>(result);
        var wire = JsonSerializer.SerializeToElement(ok.Value, WireJson);
        Assert.Equal("\"Editable\"", wire.GetProperty("Mode").GetRawText());
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_ProviderNameInvalid()
    {
        var result = await InvokeAsync("   ", Location, row: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProviderErrorCodes.InvalidName, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_QueryReturnsNull()
    {
        // The scoped query returns null both for a genuinely-unknown pair and one hidden out of scope; the
        // endpoint cannot tell them apart, so addressing by the natural key leaks no existence (ADR-0018 rider).
        var result = await InvokeAsync(Provider, "ghost", row: null);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("location.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsAdmin()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(Provider, Location, Sample(), AdminCaller(), s => { captured = s; seen = true; });

        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_CallerIsProviderIdentity()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(Provider, Location, Sample(), ProviderCaller(), s => { captured = s; seen = true; });

        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_AllowAnyActive_When_CallerIsServiceKey()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync("other", Location, Sample(), ServiceKeyProviderCaller(), s => { captured = s; seen = true; });

        // A service key resolves any active location, including one outside its own provider (ADR-0018).
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassNullScope_When_InteractiveProviderAdminReadsOwnProvider()
    {
        IReadOnlyList<ProjectShortName>? captured = null;
        var seen = false;

        await InvokeAsync(Provider, Location, Sample(), InteractiveProviderAdminCaller(), s => { captured = s; seen = true; });

        // Additive widening: a human ProviderAdmin reaches their own provider's locations without needing
        // a project link, which is more than they can see today.
        Assert.True(seen);
        Assert.Null(captured);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_InteractiveProviderAdminReadsAnotherProvider()
    {
        IReadOnlyList<ProjectShortName>? captured = null;

        await InvokeAsync("other", Location, Sample(), InteractiveProviderAdminCaller(), s => captured = s);

        // Containment: outside their own provider the reachability gate still applies, so another
        // provider's location resolves only through one of the caller's projects.
        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }

    [Fact]
    public async Task Should_PassCallerProjects_When_CallerIsUser()
    {
        IReadOnlyList<ProjectShortName>? captured = null;

        await InvokeAsync(Provider, Location, Sample(), UserCaller(), s => captured = s);

        Assert.NotNull(captured);
        Assert.Contains(ShortName, captured!);
    }
}
