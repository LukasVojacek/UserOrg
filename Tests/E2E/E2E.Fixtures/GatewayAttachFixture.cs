using System.Data;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;

namespace E2E.Fixtures;

/// <summary>
/// Attach-mode E2E test fixture that seeds and drives an already-running topology (API, Gateway, SQL,
/// WireMock) via the <c>PARITY_*</c> environment exported by <c>TestcontainersTopologyFixture</c>. Provides
/// deterministic seed data, per-role dev-auth, role-scoped client factories, and database snapshots for
/// fast resets. It never boots its own host; the topology it attaches to is owned by the assembly fixture.
/// </summary>
public sealed class GatewayAttachFixture : IAsyncLifetime
{
    private const string DatabaseResourceName = "UserOrgDBContext";
    private const string SeedProjectId = "11111111-1111-1111-1111-111111111111";
    private const string SeedRequestId = "44444444-4444-4444-4444-444444444444";
    private const string RuntimeRequestId = "77777777-7777-7777-7777-777777777777";
    private const string ProjectManagerRequestId = "66666666-7777-8888-9999-AAAAAAAAAAAA";
    private const string ProjectMemberRequestId = "BBBBBBBB-7777-8888-9999-AAAAAAAAAAAA";
    private const string PlatformAdminUserId = "DDDDDDDD-DDDD-DDDD-DDDD-DDDDDDDDDDDD";
    private const string ProjectOwnerUserId = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
    private const string ProjectManagerUserId = "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB";
    private const string ProjectMemberUserId = "CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC";
    private const int ProjectManagerRoleId = 3;

    private HttpClient? _gatewayClient;

    // The fixture connects to an ALREADY-RUNNING topology (child API + Gateway + SQL container +
    // WireMock) exported by TestcontainersTopologyFixture through the PARITY_* environment. All the
    // seeding / snapshot / dev-auth SQL below runs against that live database. Preconditions on the
    // running topology: DevAuthOverride enabled, and the HEAppE WireMock reachable if HEAppE-backed
    // specs are exercised.
    private Uri? _gatewayBaseUri;
    private string? _connectionString;
    private string? _heappeMockUrl;

    public const string ProjectOwnerUserName = "test";
    public const string ProjectManagerUserName = "test2";
    public const string ProjectMemberUserName = "test3";
    public const string SharedUserPassword = "test";

    // Fixed idpids for the dev-auth override (ADR-0045). Each request carries one of these as its
    // bearer; the gateway impersonates it. No IdP/Keycloak. Must satisfy IdpId validation.
    private const string OwnerIdpId = "e2e-owner-idp";
    private const string ManagerIdpId = "e2e-manager-idp";
    private const string MemberIdpId = "e2e-member-idp";
    private const string AdminIdpId = "e2e-admin-idp";

    /// <summary>
    /// The seeded ProviderAdmin's identity-provider id (<c>seed-data.sql</c> coverage block). The row it
    /// names carries the ProviderAdmin system role and <c>AssignedProviderId</c> = <see cref="OwnedProviderName"/>,
    /// which is what makes the Gateway put a <c>provider_name</c> claim on its internal token (ADR-0011 D5).
    /// </summary>
    private const string ProviderAdminIdpId = "0d000000-0000-0000-0000-000000000007";

    /// <summary>The provider the seeded ProviderAdmin is assigned to, and therefore may write.</summary>
    public const string OwnedProviderName = "prov1";

    /// <summary>A seeded provider the ProviderAdmin is NOT assigned to, and therefore may not write.</summary>
    public const string ForeignProviderName = "prov2";

    /// <summary>
    /// Gets the authenticated Gateway HTTP client (platform-admin identity). Throws if fixture not initialized.
    /// </summary>
    public HttpClient GatewayClient => _gatewayClient ?? throw new InvalidOperationException("Gateway client has not been initialized.");

    /// <summary>
    /// The dev-auth token for platform-admin (formatted as the admin's idpid under ADR-0045).
    /// </summary>
    public string AccessToken { get; private set; } = string.Empty;

    /// <summary>
    /// The Guid of the runtime user (bootstrapped from /api/UserInfo after seed).
    /// </summary>
    public Guid RuntimeUserId { get; private set; }

    /// <summary>
    /// The dev-auth IdP ID of the runtime platform-admin user.
    /// </summary>
    public string RuntimeUserIdentityProviderId { get; private set; } = string.Empty;

    /// <summary>
    /// The seeded project-owner identity provider ID (used for role-scoped client creation).
    /// </summary>
    public string ProjectOwnerIdentityProviderId { get; private set; } = string.Empty;

    /// <summary>
    /// The seeded project-manager identity provider ID (used for role-scoped client creation).
    /// </summary>
    public string ProjectManagerIdentityProviderId { get; private set; } = string.Empty;

    /// <summary>
    /// The seeded project-member identity provider ID (used for role-scoped client creation).
    /// </summary>
    public string ProjectMemberIdentityProviderId { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var gatewayUrl = Environment.GetEnvironmentVariable("PARITY_GATEWAY_URL");
        if (string.IsNullOrWhiteSpace(gatewayUrl))
        {
            throw new InvalidOperationException(
                "PARITY_GATEWAY_URL is not set. TestcontainersTopologyFixture must start and export the PARITY_* environment before this attach-mode fixture initializes.");
        }

        _gatewayBaseUri = new Uri(gatewayUrl, UriKind.Absolute);
        _connectionString = Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? "Server=localhost,1433;Database=UserOrgDBContext;User Id=sa;Password=userorg-dev-pw;TrustServerCertificate=True";
        // Only needed if HEAppE-backed specs run; seed replaces __HEAPPE_MOCK_URL__ with it.
        _heappeMockUrl = Environment.GetEnvironmentVariable("PARITY_HEAPPE_MOCK_URL");

        _gatewayClient = NewRawGatewayClient();

        await WaitForDatabaseAsync(cancellationToken);
        await WaitForGatewayAsync(cancellationToken);
        await ResolveSeedIdentityProviderIdsAsync(cancellationToken);
        await SeedDatabaseWithRetryAsync(cancellationToken);

        AccessToken = await AcquireAccessTokenAsync(cancellationToken);
        _gatewayClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

        await EnsurePlatformAdminAccessAsync(cancellationToken);
        await BootstrapRuntimeUserAsync(cancellationToken);
        await EnsureRuntimeProjectAccessAsync(cancellationToken);

        // Capture the full baseline (seed + runtime bootstrap) as a snapshot so the few
        // tests that need a pristine DB can revert to it via ResetToSeedAsync (ADR-0052).
        await CreateSeedSnapshotAsync(cancellationToken);
    }

    // Raw (unauthenticated) Gateway client — a plain HttpClient pointed at the running gateway, with
    // dev-cert validation bypassed to tolerate the topology's self-signed HTTPS.
    private HttpClient NewRawGatewayClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        return new HttpClient(handler) { BaseAddress = _gatewayBaseUri };
    }

    public ValueTask DisposeAsync()
    {
        _gatewayClient?.Dispose();
        // The attached topology (containers, child processes) is owned by TestcontainersTopologyFixture
        // and left intact here.
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Creates an unauthenticated Gateway HTTP client (no Authorization header).
    /// </summary>
    public HttpClient CreateAnonymousGatewayClient()
    {
        return NewRawGatewayClient();
    }

    /// <summary>
    /// Creates an authenticated Gateway HTTP client with platform-admin credentials.
    /// </summary>
    public HttpClient CreateAdminGatewayClient()
    {
        var client = NewRawGatewayClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        return client;
    }

    /// <summary>
    /// Creates an authenticated Gateway HTTP client as the seeded ProviderAdmin, whose token carries the
    /// <c>provider_name</c> claim for <see cref="OwnedProviderName"/>. Needs no pre-seeding: the role and
    /// the provider assignment both come from <c>seed-data.sql</c>.
    /// </summary>
    public HttpClient CreateProviderAdminGatewayClient()
    {
        var client = NewRawGatewayClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProviderAdminIdpId);
        return client;
    }

    /// <summary>
    /// Creates an authenticated Gateway HTTP client as a project owner (Owner role).
    /// Cleans up non-seed state and ensures seeded project membership before returning.
    /// </summary>
    public async Task<HttpClient> CreateProjectOwnerClientAsync(CancellationToken cancellationToken)
    {
        await RemoveNonSeedProjectStateForUserAsync(ProjectOwnerUserId, cancellationToken);
        await EnsureSeedUserAccessAsync(
            ProjectOwnerUserId,
            ProjectOwnerIdentityProviderId,
            ProjectOwnerIdentityProviderId,
            "test@test.cz",
            "Owner",
            cancellationToken);
        return await CreateAuthenticatedGatewayClientAsync(ProjectOwnerUserName, SharedUserPassword, cancellationToken);
    }

    /// <summary>
    /// Creates an authenticated Gateway HTTP client as a project manager (Manager role).
    /// Cleans up non-seed state and ensures seeded project membership before returning.
    /// </summary>
    public async Task<HttpClient> CreateProjectManagerClientAsync(CancellationToken cancellationToken)
    {
        await RemoveNonSeedProjectStateForUserAsync(ProjectManagerUserId, cancellationToken);
        await EnsureSeedUserAccessAsync(
            ProjectManagerUserId,
            ProjectManagerIdentityProviderId,
            ProjectManagerIdentityProviderId,
            "test2@test.cz",
            "Manager",
            cancellationToken);
        return await CreateAuthenticatedGatewayClientAsync(ProjectManagerUserName, SharedUserPassword, cancellationToken);
    }

    /// <summary>
    /// Creates an authenticated Gateway HTTP client as a project member (Member role).
    /// Cleans up non-seed state and ensures seeded project membership before returning.
    /// </summary>
    public async Task<HttpClient> CreateProjectMemberClientAsync(CancellationToken cancellationToken)
    {
        await RemoveNonSeedProjectStateForUserAsync(ProjectMemberUserId, cancellationToken);
        await EnsureSeedUserAccessAsync(
            ProjectMemberUserId,
            ProjectMemberIdentityProviderId,
            ProjectMemberIdentityProviderId,
            "test3@test.cz",
            "Member",
            cancellationToken);
        return await CreateAuthenticatedGatewayClientAsync(ProjectMemberUserName, SharedUserPassword, cancellationToken);
    }

    /// <summary>
    /// Ensures a deterministic request exists for the given role and returns its ID.
    /// Maps role names (admin, owner, manager, member) to canonical seed or runtime request IDs.
    /// </summary>
    public async Task<Guid> EnsureSentRequestForRoleAsync(string role, CancellationToken cancellationToken)
    {
        return role switch
        {
            "admin" => Guid.Parse(RuntimeRequestId),
            "owner" => Guid.Parse(SeedRequestId),
            "manager" => await EnsureProjectRequestForUserAsync(ProjectManagerUserId, ProjectManagerRequestId, cancellationToken).ConfigureAwait(false),
            "member" => await EnsureProjectRequestForUserAsync(ProjectMemberUserId, ProjectMemberRequestId, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported role variant.")
        };
    }

    private async Task WaitForDatabaseAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) 1
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'User'
            """;

        await RetryAsync(
            async () =>
            {
                await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
                await connection.OpenAsync(cancellationToken);

                await using var command = connection.CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandText = sql;

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result is not null;
            },
            TimeSpan.FromMinutes(5),
            cancellationToken,
            "Database schema was not ready for endpoint test seeding.");
    }

    private async Task WaitForGatewayAsync(CancellationToken cancellationToken)
    {
        await RetryAsync(
            async () =>
            {
                using var response = await GatewayClient.GetAsync("/api/version", cancellationToken);
                return (int)response.StatusCode < 500;
            },
            TimeSpan.FromMinutes(5),
            cancellationToken,
            "Gateway did not become reachable for endpoint tests.");
    }

    private async Task SeedDatabaseAsync(CancellationToken cancellationToken)
    {
        var seedScriptPath = Path.Combine(AppContext.BaseDirectory, "seed-data.sql");
        var seedScript = await File.ReadAllTextAsync(seedScriptPath, cancellationToken);
        var heappeMockUrl = ResolveHeappeMockUrl();

        seedScript = seedScript
            .Replace("__PROJECT_OWNER_IDP_ID__", ProjectOwnerIdentityProviderId, StringComparison.Ordinal)
            .Replace("__PROJECT_MANAGER_IDP_ID__", ProjectManagerIdentityProviderId, StringComparison.Ordinal)
            .Replace("__PROJECT_MEMBER_IDP_ID__", ProjectMemberIdentityProviderId, StringComparison.Ordinal)
            .Replace("__HEAPPE_MOCK_URL__", heappeMockUrl, StringComparison.Ordinal);
        var batches = Regex.Split(seedScript, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        foreach (var batch in batches)
        {
            await using var command = connection.CreateCommand();
            command.CommandType = CommandType.Text;
            command.CommandText = batch;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private string ResolveHeappeMockUrl()
    {
        return _heappeMockUrl?.TrimEnd('/')
            ?? throw new InvalidOperationException(
                "PARITY_HEAPPE_MOCK_URL must be set for HEAppE-backed specs (the topology fixture must export the running HEAppE mock endpoint).");
    }

    private async Task SeedDatabaseWithRetryAsync(CancellationToken cancellationToken)
    {
        await RetryAsync(
            async () =>
            {
                // Let exceptions propagate to RetryAsync so the real cause is captured as the inner
                // exception on final failure (transient DB-not-ready errors are simply retried).
                await SeedDatabaseAsync(cancellationToken);
                return true;
            },
            TimeSpan.FromMinutes(5),
            cancellationToken,
            "Seed data could not be loaded into the database.");
    }

    private Task<string> AcquireAccessTokenAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // The fixture's default gateway client authenticates as the platform admin. With the dev-auth
        // override the "token" is simply that admin's idpid, carried as the bearer.
        return Task.FromResult(AdminIdpId);
    }

    private Task ResolveSeedIdentityProviderIdsAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        // Identities are fixed idpids we own and seed (dev-auth override, no IdP round-trip).
        ProjectOwnerIdentityProviderId = OwnerIdpId;
        ProjectManagerIdentityProviderId = ManagerIdpId;
        ProjectMemberIdentityProviderId = MemberIdpId;
        return Task.CompletedTask;
    }

    private async Task<HttpClient> CreateAuthenticatedGatewayClientAsync(string username, string password, CancellationToken cancellationToken)
    {
        var client = NewRawGatewayClient();
        var accessToken = await AcquirePasswordGrantTokenAsync(username, password, cancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private Task<string> AcquirePasswordGrantTokenAsync(string username, string password, CancellationToken cancellationToken)
    {
        _ = password;
        _ = cancellationToken;
        // The "bearer" is the user's idpid — the gateway dev-auth override impersonates whatever idpid
        // the bearer carries. No IdP round-trip, no per-user token command.
        var idpId = username switch
        {
            ProjectOwnerUserName => OwnerIdpId,
            ProjectManagerUserName => ManagerIdpId,
            ProjectMemberUserName => MemberIdpId,
            _ => throw new InvalidOperationException($"No idpid is configured for user '{username}'.")
        };
        return Task.FromResult(idpId);
    }

    private async Task EnsureSeedUserAccessAsync(
        string userId,
        string identityProviderUserId,
        string userName,
        string email,
        string roleName,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            IF EXISTS (
                SELECT 1
                FROM [dbo].[User]
                WHERE [Id] = @userId)
            BEGIN
                UPDATE [dbo].[User]
                SET [IdentityProviderUserId] = @identityProviderUserId,
                    [Email] = @email,
                    [UserName] = @userName
                WHERE [Id] = @userId;
            END
            ELSE
            BEGIN
                INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
                VALUES (@userId, @identityProviderUserId, @email, @userName);
            END;

            DECLARE @systemRoleId INT = (
                SELECT TOP (1) r.[Id]
                FROM [forge].[Roles] r
                WHERE r.[Name] = @roleName);

            IF @systemRoleId IS NULL
            BEGIN
                THROW 50003, 'Project role was not found in forge.Roles for E2E user bootstrap.', 1;
            END;

            IF EXISTS (
                SELECT 1
                FROM [dbo].[UserPermission]
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId)
            BEGIN
                UPDATE [dbo].[UserPermission]
                SET [SystemRoleId] = @systemRoleId
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId;
            END
            ELSE
            BEGIN
                INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
                VALUES (@userPermissionId, @projectId, @userId, @systemRoleId);
            END;

            -- Every user must carry exactly one system (ProjectId IS NULL) permission — the v2 userinfo
            -- path enforces it (DatabaseContext.UserRepository.ValidateSystemRole). Seed the baseline
            -- 'User' system role, mirroring how EnsurePlatformAdminAccessAsync seeds Admin's system row.
            DECLARE @baselineSystemRoleId INT = (
                SELECT TOP (1) r.[Id]
                FROM [forge].[Roles] r
                WHERE r.[Name] = 'User');

            IF @baselineSystemRoleId IS NULL
            BEGIN
                THROW 50004, 'Baseline ''User'' system role was not found in forge.Roles for E2E user bootstrap.', 1;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[UserPermission]
                WHERE [ProjectId] IS NULL
                  AND [UserId] = @userId)
            BEGIN
                INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
                VALUES (@baselineUserPermissionId, NULL, @userId, @baselineSystemRoleId);
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[AssignedUserToProject]
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId)
            BEGIN
                INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
                VALUES (@projectId, @userId, @userId);
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@userPermissionId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@baselineUserPermissionId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@projectId", Guid.Parse(SeedProjectId));
        command.Parameters.AddWithValue("@userId", Guid.Parse(userId));
        command.Parameters.AddWithValue("@identityProviderUserId", identityProviderUserId);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@userName", userName);
        command.Parameters.AddWithValue("@roleName", roleName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task EnsurePlatformAdminAccessAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DECLARE @systemRoleId INT = (
                SELECT TOP (1) r.[Id]
                FROM [forge].[Roles] r
                WHERE r.[Name] = 'Admin');

            IF @systemRoleId IS NULL
            BEGIN
                THROW 50002, 'Admin role was not found in forge.Roles for E2E bootstrap.', 1;
            END;

            IF EXISTS (
                SELECT 1
                FROM [dbo].[User]
                WHERE [Id] = @userId)
            BEGIN
                UPDATE [dbo].[User]
                SET [IdentityProviderUserId] = @identityProviderUserId,
                    [Email] = @email,
                    [UserName] = @userName
                WHERE [Id] = @userId;
            END
            ELSE
            BEGIN
                INSERT INTO [dbo].[User] (Id, IdentityProviderUserId, Email, UserName)
                VALUES (@userId, @identityProviderUserId, @email, @userName);
            END;

            IF EXISTS (
                SELECT 1
                FROM [dbo].[UserPermission]
                WHERE [ProjectId] IS NULL
                  AND [UserId] = @userId)
            BEGIN
                UPDATE [dbo].[UserPermission]
                SET [SystemRoleId] = @systemRoleId
                WHERE [ProjectId] IS NULL
                  AND [UserId] = @userId;
            END
            ELSE
            BEGIN
                INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
                VALUES (@userPermissionId, NULL, @userId, @systemRoleId);
            END;

            -- Non-trivial preferences blob for the admin caller; the seed users get theirs from
            -- seed-data.sql, which runs before this user row exists. Without it UserParityTests
            -- compares the "{}" both stacks coalesce a missing row to.
            IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[UserPreference]
                WHERE [UserId] = @userId)
            BEGIN
                INSERT INTO [dbo].[UserPreference] (Id, UserId, Data)
                VALUES (@preferenceId, @userId, @preferenceData);
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@userPermissionId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@userId", Guid.Parse(PlatformAdminUserId));
        command.Parameters.AddWithValue("@preferenceId", Guid.Parse("EEEEEEEE-EEEE-EEEE-EEEE-EEEEEEEEEEE4"));
        command.Parameters.AddWithValue(
            "@preferenceData",
            """{"theme":"dark","dashboard":{"layout":{"columns":3,"compact":true},"widgets":["projects","resources","requests"]},"notifications":{"email":true,"digest":"weekly","mutedUntil":null},"owner":"e2e-admin"}""");
        command.Parameters.AddWithValue("@identityProviderUserId", AccessToken);
        command.Parameters.AddWithValue("@email", "admin@userorg.test");
        // UserName must equal the idpid: the dev-auth override forwards preferred_username = sub,
        // and the legacy user-init filter looks the user up by preferred_username.
        command.Parameters.AddWithValue("@userName", AccessToken);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task BootstrapRuntimeUserAsync(CancellationToken cancellationToken)
    {
        using var response = await GatewayClient.GetAsync("/api/UserInfo", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"/api/UserInfo returned {(int)response.StatusCode}: {body}");
        }

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        RuntimeUserId = payload.GetProperty("Id").GetGuid();
        RuntimeUserIdentityProviderId = payload.GetProperty("KeycloakSid").GetString()
            ?? throw new InvalidOperationException("UserInfo response does not contain KeycloakSid.");
    }

    private async Task EnsureRuntimeProjectAccessAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[AssignedUserToProject]
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId)
            BEGIN
                INSERT INTO [dbo].[AssignedUserToProject] (ProjectId, UserId, AssignedByUserId)
                VALUES (@projectId, @userId, @userId)
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[UserPermission]
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId
                  AND [SystemRoleId] = @systemRoleId)
            BEGIN
                INSERT INTO [dbo].[UserPermission] (Id, ProjectId, UserId, SystemRoleId)
                VALUES (@userPermissionId, @projectId, @userId, @systemRoleId)
            END;

            IF EXISTS (
                SELECT 1
                FROM [dbo].[Request]
                WHERE [Id] = @requestId)
            BEGIN
                UPDATE [dbo].[Request]
                SET [ProjectId] = @projectId,
                    [UserId] = @userId,
                    [State] = 1,
                    [Type] = 0
                WHERE [Id] = @requestId;
            END
            ELSE IF NOT EXISTS (
                SELECT 1
                FROM [dbo].[Request]
                WHERE [ProjectId] = @projectId
                  AND [UserId] = @userId)
            BEGIN
                INSERT INTO [dbo].[Request] (Id, ProjectId, UserId, State, Type)
                VALUES (@requestId, @projectId, @userId, 1, 0)
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@userPermissionId", Guid.CreateVersion7());
        command.Parameters.AddWithValue("@projectId", Guid.Parse(SeedProjectId));
        command.Parameters.AddWithValue("@requestId", Guid.Parse(RuntimeRequestId));
        command.Parameters.AddWithValue("@userId", RuntimeUserId);
        command.Parameters.AddWithValue("@systemRoleId", ProjectManagerRoleId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<Guid> EnsureProjectRequestForUserAsync(string userId, string requestId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            IF EXISTS (
                SELECT 1
                FROM [dbo].[Request]
                WHERE [Id] = @requestId)
            BEGIN
                UPDATE [dbo].[Request]
                SET [ProjectId] = @projectId,
                    [UserId] = @userId,
                    [State] = 1,
                    [Type] = 2
                WHERE [Id] = @requestId;
            END
            ELSE
            BEGIN
                INSERT INTO [dbo].[Request] (Id, ProjectId, UserId, State, Type)
                VALUES (@requestId, @projectId, @userId, 1, 2);
            END;
            """;

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@projectId", Guid.Parse(SeedProjectId));
        command.Parameters.AddWithValue("@requestId", Guid.Parse(requestId));
        command.Parameters.AddWithValue("@userId", Guid.Parse(userId));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Guid.Parse(requestId);
    }

    private async Task RemoveNonSeedProjectStateForUserAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            DELETE FROM [dbo].[Request]
            WHERE [UserId] = @userId
              AND [ProjectId] <> @projectId;

            DELETE FROM [dbo].[AssignedUserToProject]
            WHERE [UserId] = @userId
              AND [ProjectId] <> @projectId;

            DELETE FROM [dbo].[UserPermission]
            WHERE [UserId] = @userId
              AND [ProjectId] IS NOT NULL
              AND [ProjectId] <> @projectId;
            """;

        await using var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@userId", Guid.Parse(userId));
        command.Parameters.AddWithValue("@projectId", Guid.Parse(SeedProjectId));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private Task<string> GetConnectionStringAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(_connectionString
            ?? throw new InvalidOperationException("SQL connection string was not configured."));
    }

    private const string SeedSnapshotName = "UserOrgDBContext_seed_snapshot";

    // Seeded project always present in the baseline; used as a lightweight DB-backed probe to
    // confirm the API has recovered end-to-end after a snapshot reset.
    private const string SeedResetReadinessProbeShortName = "TESTPRJ";

    // Large enough to exceed either stack's pooled connection count, so the burst forces every
    // broken pooled connection to be handed out (and evicted) within this one gate.
    private const int ConcurrentProbeBurstSize = 20;

    private bool _seedSnapshotReady;

    private async Task<string> GetMasterConnectionStringAsync(CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(await GetConnectionStringAsync(cancellationToken))
        {
            InitialCatalog = "master"
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Takes a SQL Server database snapshot of the fully-seeded database. Best-effort:
    /// snapshots require Developer/Enterprise edition, so failure is non-fatal and simply
    /// disables the fast reset path (<see cref="ResetToSeedAsync"/> then falls back to re-seeding).
    /// </summary>
    private async Task CreateSeedSnapshotAsync(CancellationToken cancellationToken)
    {
        const string createSnapshotSql = $"""
            IF DB_ID(N'{SeedSnapshotName}') IS NOT NULL
                DROP DATABASE [{SeedSnapshotName}];

            DECLARE @logicalName sysname, @physical nvarchar(4000), @ssFile nvarchar(4000), @sql nvarchar(max);
            SELECT TOP 1 @logicalName = name, @physical = physical_name
            FROM sys.master_files
            WHERE database_id = DB_ID(N'{DatabaseResourceName}') AND type = 0;

            SET @ssFile = REPLACE(@physical, '.mdf', '_seedsnap.ss');
            SET @sql = N'CREATE DATABASE [{SeedSnapshotName}] ON (NAME = ' + QUOTENAME(@logicalName)
                + N', FILENAME = ''' + @ssFile + N''') AS SNAPSHOT OF [{DatabaseResourceName}];';
            EXEC sp_executesql @sql;
            """;

        try
        {
            await using var connection = new SqlConnection(await GetMasterConnectionStringAsync(cancellationToken));
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = createSnapshotSql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _seedSnapshotReady = true;
        }
        catch (SqlException ex)
        {
            // Edition without snapshot support (e.g. Express) — degrade gracefully.
            _seedSnapshotReady = false;
            System.Diagnostics.Debug.WriteLine($"Seed snapshot unavailable, reset will re-seed instead: {ex.Message}");
        }
    }

    /// <summary>
    /// Resets the database to the seeded baseline via a SQL snapshot or full re-seed.
    /// Reverts all runtime mutations; use only for tests requiring a pristine global state (ADR-0052).
    /// </summary>
    public async Task ResetToSeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_seedSnapshotReady)
        {
            await SeedDatabaseAsync(cancellationToken);
            return;
        }

        const string revertSql = $"""
            ALTER DATABASE [{DatabaseResourceName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
            RESTORE DATABASE [{DatabaseResourceName}] FROM DATABASE_SNAPSHOT = N'{SeedSnapshotName}';
            ALTER DATABASE [{DatabaseResourceName}] SET MULTI_USER;
            """;

        await using (var connection = new SqlConnection(await GetMasterConnectionStringAsync(cancellationToken)))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = revertSql;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // SINGLE_USER + snapshot restore severs the API's pooled sessions and rejects app-catalog
        // logins (SqlException 18456) for a short window after MULTI_USER returns. Without this gate
        // the next sequential test races the recovery and gets a 500. Wait until the app database
        // serves logins again before returning; this only ever waits longer, never does less.
        await WaitForSeedResetReadyAsync(cancellationToken);
    }

    /// <summary>
    /// Bounded readiness gate after a snapshot restore: does not return until the app database
    /// accepts app-catalog logins again and the API can serve a DB-backed request end-to-end
    /// (proving its connection pool has recovered from the severed sessions).
    /// </summary>
    private async Task WaitForSeedResetReadyAsync(CancellationToken cancellationToken)
    {
        await RetryAsync(
            async () =>
            {
                await using var connection = new SqlConnection(await GetConnectionStringAsync(cancellationToken));
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1";
                return await command.ExecuteScalarAsync(cancellationToken) is not null;
            },
            TimeSpan.FromSeconds(30),
            cancellationToken,
            "App database did not accept logins again after snapshot reset.");

        await RetryAsync(
            async () =>
            {
                using var owner = await CreateProjectOwnerClientAsync(cancellationToken);
                using var response = await owner.GetAsync(
                    new Uri($"/api/v2/Projects/{SeedResetReadinessProbeShortName}", UriKind.Relative),
                    cancellationToken);
                return response.IsSuccessStatusCode;
            },
            TimeSpan.FromSeconds(30),
            cancellationToken,
            "API did not serve a DB-backed request again after snapshot reset.");

        // The severed sessions above don't all get evicted by the single sequential probe: the API's
        // ADO.NET pool hands out whichever pooled (possibly still-broken) connection is next, one at a
        // time, so a broken connection only surfaces — and gets evicted — the first time some request
        // happens to draw it. A single successful sequential probe only proves ONE connection recovered;
        // the next several DIFFERENT tests would each eat one 500 discovering the rest. Worse, the legacy
        // (Infrastructure.DataAccess) and modular (Core.DataAccess) stacks open connections differently
        // (ongoing DDD migration, CLAUDE.md) and recover independently of each other, so a v2-only probe
        // proves nothing about the legacy v1 read path. Firing a burst of CONCURRENT requests against
        // both stacks forces each pool to hand out many connections at once, evicting every broken one in
        // this one gate instead of leaking eviction failures into whichever specs run next.
        await RetryAsync(
            async () =>
            {
                using var owner = await CreateProjectOwnerClientAsync(cancellationToken);
                using var admin = CreateAdminGatewayClient();
                var v2Probes = Enumerable.Range(0, ConcurrentProbeBurstSize).Select(async _ =>
                {
                    using var response = await owner.GetAsync(
                        new Uri($"/api/v2/Projects/{SeedResetReadinessProbeShortName}", UriKind.Relative),
                        cancellationToken);
                    return response.IsSuccessStatusCode;
                });
                var v1Probes = Enumerable.Range(0, ConcurrentProbeBurstSize).Select(async _ =>
                {
                    using var response = await admin.GetAsync(new Uri("/api/Administration/User", UriKind.Relative), cancellationToken);
                    return response.IsSuccessStatusCode;
                });
                var outcomes = await Task.WhenAll(v2Probes.Concat(v1Probes));
                return Array.TrueForAll(outcomes, succeeded => succeeded);
            },
            TimeSpan.FromSeconds(30),
            cancellationToken,
            "API's connection pool did not fully recover (concurrent probe burst) after snapshot reset.");
    }

    private static async Task RetryAsync(
        Func<Task<bool>> action,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string failureMessage)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await action().ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (Exception exception)
            {
                lastException = exception;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException(failureMessage, lastException);
    }
}
