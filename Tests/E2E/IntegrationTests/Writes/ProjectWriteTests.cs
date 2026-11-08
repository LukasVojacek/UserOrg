using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using E2E.Fixtures;

using Microsoft.Data.SqlClient;

namespace IntegrationTests.Writes;

/// <summary>
/// Synchronous v2 project writes (create, update) through the Gateway: both the immediate HTTP
/// response and the DB row are asserted right after the call — no worker, no polling.
/// </summary>
/// <remarks>
/// Resets to the seed baseline after each test (<see cref="DisposeAsync"/>) so a mutation here
/// (a new project row, or a renamed <c>TESTPRJ</c>) never leaks into another spec sharing this
/// assembly's <see cref="GatewayAttachFixture"/> instance.
/// </remarks>
[Collection(ParityTests.ParityTestCollection.Name)]
public sealed class ProjectWriteTests(GatewayAttachFixture fixture) : IAsyncLifetime
{
    private const string SeededProjectShortName = "TESTPRJ";
    private const string DomainTypeName = "Agricultural and Veterinary Sciences";
    private const int DomainTypeId = 1;
    private const string SeededProviderName = "prov1";
    private const string UnlinkedResourceName = "assignupdate";

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await fixture.ResetToSeedAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Should_CreateProject_When_OwnerPostsValidRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);
        var shortName = NewShortName();

        using var response = await owner.PostAsJsonAsync(ProjectsUri, CreateProjectRequest(shortName), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected 201 for project create, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(shortName, document.RootElement.GetProperty("ShortName").GetString());

        var (name, domainTypeId) = await ReadProjectAsync(shortName, ct);
        Assert.Equal("E2E created project", name);
        Assert.Equal(DomainTypeId, domainTypeId);
    }

    [Fact]
    public async Task Should_ReturnCreatedProject_When_CreatorGetsItThroughGateway()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);
        var shortName = NewShortName();

        // why: the warm-up read caches this caller's permission snapshot in the Gateway before the create,
        // so a 200 afterwards proves the create evicted it (#337); a cold cache would pass without the eviction.
        using var warmUp = await owner.GetAsync(ProjectUri(SeededProjectShortName), ct);
        await AssertStatusAsync(warmUp, HttpStatusCode.OK, "warm-up read", ct);

        using var created = await owner.PostAsJsonAsync(ProjectsUri, CreateProjectRequest(shortName), ct);
        await AssertStatusAsync(created, HttpStatusCode.Created, "project create", ct);

        using var read = await owner.GetAsync(ProjectUri(shortName), ct);
        var body = await read.Content.ReadAsStringAsync(ct);
        Assert.True(
            read.StatusCode == HttpStatusCode.OK,
            $"Expected 200 for the created project read, got {(int)read.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        Assert.Equal(shortName, document.RootElement.GetProperty("ShortName").GetString());
    }

    [Fact]
    public async Task Should_UpdateProject_When_OwnerPutsValidRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var request = new JsonObject
        {
            ["Name"] = "TESTPRJ renamed by ProjectWriteTests",
            ["Description"] = "Updated by ProjectWriteTests.",
            ["ContactEmail"] = "owner@test.cz",
            ["StartDate"] = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            ["EndDate"] = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero),
            ["DomainTypeName"] = DomainTypeName,
        };

        using var response = await owner.PutAsJsonAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}", UriKind.Relative), request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204 for project update, got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        var (name, _) = await ReadProjectAsync(SeededProjectShortName, ct);
        Assert.Equal("TESTPRJ renamed by ProjectWriteTests", name);
    }

    [Fact]
    public async Task Should_UnassignProjectUser_When_OwnerDeletesActiveMember()
    {
        var ct = TestContext.Current.CancellationToken;
        (await fixture.CreateProjectMemberClientAsync(ct)).Dispose();
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        using var response = await owner.DeleteAsync(
            new Uri($"/api/v2/Projects/{SeededProjectShortName}/Users/{fixture.ProjectMemberIdentityProviderId}", UriKind.Relative), ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204 for project user unassign, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    [Fact]
    public async Task Should_UnassignProjectResource_When_AdminDeletesAssignedResource()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();
        var link = ProjectResourceUri();

        using var assigned = await admin.PutAsync(link, content: null, ct);
        await AssertStatusAsync(assigned, HttpStatusCode.NoContent, "resource assign", ct);

        using var unassigned = await admin.DeleteAsync(link, ct);
        await AssertStatusAsync(unassigned, HttpStatusCode.NoContent, "resource unassign", ct);

        Assert.Equal([true], await ReadLinkStatesAsync(ct));
    }

    [Fact]
    public async Task Should_ReassignProjectResource_When_AdminReassignsUnassignedResource()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = fixture.CreateAdminGatewayClient();
        var link = ProjectResourceUri();

        using var assigned = await admin.PutAsync(link, content: null, ct);
        await AssertStatusAsync(assigned, HttpStatusCode.NoContent, "resource assign", ct);

        using var unassigned = await admin.DeleteAsync(link, ct);
        await AssertStatusAsync(unassigned, HttpStatusCode.NoContent, "resource unassign", ct);

        using var reassigned = await admin.PutAsync(link, content: null, ct);
        await AssertStatusAsync(reassigned, HttpStatusCode.NoContent, "resource re-assign", ct);

        Assert.Equal([false], await ReadLinkStatesAsync(ct));
    }

    [Fact]
    public async Task Should_ReassignProjectUser_When_OwnerReassignsRemovedMember()
    {
        var ct = TestContext.Current.CancellationToken;
        (await fixture.CreateProjectMemberClientAsync(ct)).Dispose();
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);
        var member = new Uri(
            $"/api/v2/Projects/{SeededProjectShortName}/Users/{fixture.ProjectMemberIdentityProviderId}", UriKind.Relative);

        using var unassigned = await owner.DeleteAsync(member, ct);
        await AssertStatusAsync(unassigned, HttpStatusCode.NoContent, "user unassign", ct);

        using var reassigned = await owner.PutAsync(member, content: null, ct);
        await AssertStatusAsync(reassigned, HttpStatusCode.NoContent, "user re-assign", ct);

        Assert.Equal([false], await ReadUserLinkStatesAsync(ct));
    }

    private static readonly Uri ProjectsUri = new("/api/v2/Projects", UriKind.Relative);

    private static Uri ProjectUri(string shortName) => new($"/api/v2/Projects/{shortName}", UriKind.Relative);

    private static string NewShortName() => $"E2ECR{Guid.NewGuid():N}"[..12];

    private static JsonObject CreateProjectRequest(string shortName) => new()
    {
        ["ShortName"] = shortName,
        ["Name"] = "E2E created project",
        ["Description"] = "Created by ProjectWriteTests.",
        ["ContactEmail"] = "owner@test.cz",
        ["StartDate"] = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ["EndDate"] = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
        ["DomainTypeName"] = DomainTypeName,
    };

    private static Uri ProjectResourceUri() => new(
        $"/api/v2/Projects/{SeededProjectShortName}/Providers/{SeededProviderName}/Resources/{UnlinkedResourceName}",
        UriKind.Relative);

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected, string step, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.True(
            response.StatusCode == expected,
            $"{step}: expected {(int)expected}, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    /// <summary>The IsDeleted flag of every TESTPRJ link row to the <c>assignupdate</c> resource of <c>prov1</c>.</summary>
    private static Task<List<bool>> ReadLinkStatesAsync(CancellationToken ct) => ReadIsDeletedAsync(
        command =>
        {
            command.CommandText = """
                SELECT link.IsDeleted
                FROM dbo.AssignedProjectResourceToProject link
                JOIN dbo.Project project ON project.Id = link.ProjectId
                JOIN dbo.ProjectResource resource ON resource.Id = link.ProjectResourceId
                JOIN dbo.Provider provider ON provider.Id = resource.ProviderId
                WHERE project.ShortName = @shortName AND resource.Name = @resourceName AND provider.Name = @providerName
                """;
            command.Parameters.AddWithValue("@shortName", SeededProjectShortName);
            command.Parameters.AddWithValue("@resourceName", UnlinkedResourceName);
            command.Parameters.AddWithValue("@providerName", SeededProviderName);
        },
        ct);

    /// <summary>The IsDeleted flag of every TESTPRJ membership row of the seeded project member.</summary>
    private Task<List<bool>> ReadUserLinkStatesAsync(CancellationToken ct) => ReadIsDeletedAsync(
        command =>
        {
            command.CommandText = """
                SELECT link.IsDeleted
                FROM dbo.AssignedUserToProject link
                JOIN dbo.Project project ON project.Id = link.ProjectId
                JOIN dbo.[User] member ON member.Id = link.UserId
                WHERE project.ShortName = @shortName AND member.IdentityProviderUserId = @idpUserId
                """;
            command.Parameters.AddWithValue("@shortName", SeededProjectShortName);
            command.Parameters.AddWithValue("@idpUserId", fixture.ProjectMemberIdentityProviderId);
        },
        ct);

    /// <summary>Runs the query <paramref name="prepare"/> sets up and returns its first column as booleans.</summary>
    private static async Task<List<bool>> ReadIsDeletedAsync(Action<SqlCommand> prepare, CancellationToken ct)
    {
        await using var connection = new SqlConnection(SqlConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        prepare(command);

        var states = new List<bool>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            states.Add(reader.GetBoolean(0));
        }

        return states;
    }

    private static string SqlConnectionString() =>
        Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION was not set by the topology fixture.");

    private static async Task<(string Name, int DomainTypeId)> ReadProjectAsync(string shortName, CancellationToken ct)
    {
        await using var connection = new SqlConnection(SqlConnectionString());
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, ProjectDomainTypeId FROM dbo.Project WHERE ShortName = @shortName";
        command.Parameters.AddWithValue("@shortName", shortName);

        await using var reader = await command.ExecuteReaderAsync(ct);
        Assert.True(await reader.ReadAsync(ct), $"No Project row found for ShortName '{shortName}'.");

        return (reader.GetString(0), reader.GetInt32(1));
    }
}
