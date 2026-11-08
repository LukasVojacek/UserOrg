using System.Text.Json;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

public sealed class OnboardingRbacMappingTests
{
    [Theory]
    [InlineData("PUT", "/api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Accept", "perm:user:request:manage")]
    [InlineData("PUT", "/api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Reject", "perm:user:request:manage")]
    [InlineData("POST", "/api/v2/Projects/{projectShortName}/OnboardingRequests", "perm:user:request")]
    [InlineData("GET", "/api/v2/Users/Me/OnboardingRequests", "perm:user:request")]
    public void Should_RestrictOnboardingEndpoint_When_MappingExistsInSeed(
        string httpMethod, string pathTemplate, string expectedPermission)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FindSeedFile()));

        var items = doc.RootElement
            .GetProperty("features")
            .GetProperty("endpointMappings")
            .GetProperty("items");

        var match = items.EnumerateArray().SingleOrDefault(item =>
            string.Equals(item.GetProperty("httpMethod").GetString(), httpMethod, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.GetProperty("pathTemplate").GetString(), pathTemplate, StringComparison.Ordinal));

        Assert.True(
            match.ValueKind == JsonValueKind.Object,
            $"No endpoint mapping found for {httpMethod} {pathTemplate}.");

        var permissions = match.GetProperty("permissionKeys")
            .EnumerateArray()
            .Select(p => p.GetString())
            .ToArray();

        Assert.Equal(new[] { expectedPermission }, permissions);
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
