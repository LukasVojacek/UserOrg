using System.Text.Json;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

public sealed class V2RbacMappingGapTests
{
    [Fact]
    public void Should_MapNestedLocationGet_When_SeedIsLoaded()
    {
        var permissions = PermissionsFor("GET", "/api/v2/Providers/{providerName}/Locations/{locationName}");

        Assert.Equal(new[] { "perm:location:list" }, permissions);
    }

    [Fact]
    public void Should_NotMapStaleFlatLocationGet_When_SeedIsLoaded()
    {
        var match = Find("GET", "/api/v2/locations/{id}");

        Assert.False(
            match.ValueKind == JsonValueKind.Object,
            "Stale flat-location mapping GET /api/v2/locations/{id} should have been removed.");
    }

    [Fact]
    public void Should_MapReactivateToProviderUpdate_When_SeedIsLoaded()
    {
        var permissions = PermissionsFor("POST", "/api/v2/Providers/{providerName}/Reactivate");

        Assert.Equal(new[] { "perm:provider:update" }, permissions);
    }

    [Fact]
    public void Should_NotMapReactivateToProviderRemove_When_SeedIsLoaded()
    {
        var permissions = PermissionsFor("POST", "/api/v2/Providers/{providerName}/Reactivate");

        Assert.DoesNotContain("perm:provider:remove", permissions);
    }

    [Fact]
    public void Should_MapResourceRequestStatusPolling_When_SeedIsLoaded()
    {
        var permissions = PermissionsFor("GET", "/api/v2/ResourceRequests/{requestId}");

        Assert.Equal(new[] { "perm:resource:create", "perm:project:resource:request" }, permissions);
    }

    [Fact]
    public void Should_MapProviderAdminResourceList_When_SeedIsLoaded()
    {
        var permissions = PermissionsFor("GET", "/api/v2/ProviderAdmin/Providers/{providerName}/Resources");

        Assert.Equal(new[] { "perm:provider:admin", "perm:platform:admin" }, permissions);
    }

    [Theory]
    [InlineData("/api/v2/AllocationUnitTypes")]
    [InlineData("/api/v2/AuthenticationTypes")]
    public void Should_MapReferenceListToAuthenticatedOnly_When_SeedIsLoaded(string pathTemplate)
    {
        var permissions = PermissionsFor("GET", pathTemplate);

        Assert.Empty(permissions);
    }

    [Fact]
    public void Should_NotMapRetiredEnumsAllocationUnitTypes_When_SeedIsLoaded()
    {
        var match = Find("GET", "/api/v2/enums/allocation-unit-types");

        Assert.False(
            match.ValueKind == JsonValueKind.Object,
            "Retired mapping GET /api/v2/enums/allocation-unit-types should have been removed.");
    }

    private static string[] PermissionsFor(string httpMethod, string pathTemplate)
    {
        var match = Find(httpMethod, pathTemplate);

        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"No endpoint mapping found for {httpMethod} {pathTemplate}.");

        return match.GetProperty("permissionKeys")
            .EnumerateArray()
            .Select(p => p.GetString()!)
            .ToArray();
    }

    private static JsonElement Find(string httpMethod, string pathTemplate)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindSeedFile()));

        var items = doc.RootElement
            .GetProperty("features")
            .GetProperty("endpointMappings")
            .GetProperty("items");

        return items.EnumerateArray()
            .Where(item =>
                string.Equals(item.GetProperty("httpMethod").GetString(), httpMethod, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.GetProperty("pathTemplate").GetString(), pathTemplate, StringComparison.Ordinal))
            .Select(item => item.Clone())
            .SingleOrDefault();
    }

    private static string FindSeedFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "roleManagmentRBAC.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate roleManagmentRBAC.json by walking up from {AppContext.BaseDirectory}");
    }
}
