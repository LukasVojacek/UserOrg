using System.Text.Json;

namespace Forge.Core.Tests;

/// <summary>
/// Validates the shipped RBAC seed file against the constraints the import enforces.
/// </summary>
/// <remarks>
/// Regression guard. EndpointMapping.EndpointName carries a unique index
/// (IX_EndpointMappings_EndpointName_Unique) and is a name, not a route — the route lives in
/// pathTemplate, and httpMethod distinguishes verbs on the same path (see the /api/ProjectTag
/// entries). Four kill-switch mappings were added using the path as the name; two of them shared a
/// path, so the import failed with "Cannot insert duplicate key row" and silently left that route
/// unmapped. An unmapped route is fail-closed 403, so the mistake only surfaces in production, at
/// the moment the endpoint is needed. Cheaper to fail here.
/// </remarks>
public sealed class RbacSeedFileTests
{
    private static JsonElement LoadSeedFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "roleManagmentRBAC.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var json = File.ReadAllText(Path.Combine(directory!.FullName, "roleManagmentRBAC.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static List<JsonElement> EndpointMappings(JsonElement root)
    {
        var mappings = new List<JsonElement>();

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    if (element.TryGetProperty("endpointName", out _))
                    {
                        mappings.Add(element);
                    }

                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }

        Walk(root);
        return mappings;
    }

    [Fact(DisplayName = "RBAC seed file has no duplicate endpointName")]
    public void SeedFile_Should_not_contain_duplicate_endpoint_names()
    {
        var mappings = EndpointMappings(LoadSeedFile());

        Assert.NotEmpty(mappings);

        var duplicates = mappings
            .Select(m => m.GetProperty("endpointName").GetString())
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact(DisplayName = "Kill-switch whitelist route is mapped to the provider/platform admin permissions")]
    public void SeedFile_Should_map_the_kill_switch_whitelist_route()
    {
        var mappings = EndpointMappings(LoadSeedFile());

        var whitelist = Assert.Single(mappings.Where(m =>
            m.GetProperty("pathTemplate").GetString() == "/api/Heappe/KillSwitch/{ProviderName}/{LocationName}/Whitelist"
            && m.GetProperty("httpMethod").GetString() == "PUT"));

        var permissions = whitelist.GetProperty("permissionKeys")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ToArray();

        Assert.Equal(["perm:provider:admin", "perm:platform:admin"], permissions);
    }

    [Fact(DisplayName = "Admin project-list route is mapped to the provider/platform admin permissions")]
    public void SeedFile_Should_map_the_admin_project_list_route()
    {
        var mappings = EndpointMappings(LoadSeedFile());

        var projectList = Assert.Single(mappings.Where(m =>
            m.GetProperty("pathTemplate").GetString() == "/api/Administration/Project"
            && m.GetProperty("httpMethod").GetString() == "GET"));

        var permissions = projectList.GetProperty("permissionKeys")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ToArray();

        Assert.Contains("perm:platform:admin", permissions);
        Assert.Contains("perm:provider:admin", permissions);
    }
}
