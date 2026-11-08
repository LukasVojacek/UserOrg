using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

namespace IntegrationTests.Writes;

/// <summary>
/// Issue #343: <c>PUT Providers/{p}/Locations/{l}/Assignments/{slug}</c> replacing a non-empty specification
/// set through the real API, its production unit of work and SQL Server. Dropping a name severed a tracked
/// required relationship and the save threw before reaching the database, which surfaced as a 500.
/// </summary>
/// <remarks>
/// Resets to the seed baseline after each test (<see cref="DisposeAsync"/>) so the created location and
/// allocation never leak into another spec sharing this assembly's <see cref="GatewayAttachFixture"/> instance.
/// </remarks>
[Collection(ParityTests.ParityTestCollection.Name)]
public sealed class AssignmentUpdateWriteTests(GatewayAttachFixture fixture) : IAsyncLifetime
{
    private const string SeededProviderName = "prov1";
    private const string ResourceName = "assignupdate";
    private const string LocationName = "e2e-assignupdate";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await fixture.ResetToSeedAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_ReplaceTheSpecifications_When_UpdateDropsChangesAndAddsNames()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();
        var assignments = new Uri($"/api/v2/Providers/{SeededProviderName}/Locations/{LocationName}/Assignments", UriKind.Relative);

        using var location = await admin.PostAsJsonAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Locations", UriKind.Relative),
            new { Name = LocationName, Endpoint = "https://hpc.example.test", LocationType = "HPC", IsPrivate = false, IsShared = true },
            ct);
        await AssertStatusAsync(location, HttpStatusCode.Created, "location create", ct);

        using var assigned = await admin.PostAsJsonAsync(
            assignments,
            new
            {
                ResourceName,
                AllocationUnitType = "NodeHours",
                AllocationUnitCount = 10,
                Specifications = new[] { new { Name = "cpu", Value = "16" }, new { Name = "mem", Value = "64" }, new { Name = "mem", Value = "128" } },
            },
            ct);
        var assignedBody = await AssertStatusAsync(assigned, HttpStatusCode.Created, "assignment create", ct);
        using var assignedDocument = JsonDocument.Parse(assignedBody);
        var slug = assignedDocument.RootElement.GetProperty("Slug").GetString();

        using var updated = await admin.PutAsJsonAsync(
            new Uri($"{assignments}/{slug}", UriKind.Relative),
            new
            {
                AllocationUnitType = "NodeHours",
                AllocationUnitCount = 20,
                Specifications = new[] { new { Name = "cpu", Value = "8" }, new { Name = "gpu", Value = "1" }, new { Name = "gpu", Value = "2" } },
            },
            ct);
        await AssertStatusAsync(updated, HttpStatusCode.NoContent, "assignment update", ct);

        using var resource = await admin.GetAsync(
            new Uri($"/api/v2/Providers/{SeededProviderName}/Resources/{ResourceName}", UriKind.Relative),
            ct);
        var resourceBody = await AssertStatusAsync(resource, HttpStatusCode.OK, "resource read-back", ct);

        using var document = JsonDocument.Parse(resourceBody);
        var assignment = Assert.Single(
            document.RootElement.GetProperty("Assignments").EnumerateArray(),
            a => a.GetProperty("Slug").GetString() == slug);
        var specifications = assignment.GetProperty("Specifications").EnumerateArray()
            .Select(s => (s.GetProperty("Name").GetString(), s.GetProperty("Value").GetString()))
            .Order()
            .ToArray();
        Assert.Equal([("cpu", "8"), ("gpu", "1"), ("gpu", "2")], specifications);
    }

    private static async Task<string> AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string step, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == expected,
            $"{step}: expected {(int)expected}, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
        return body;
    }
}
