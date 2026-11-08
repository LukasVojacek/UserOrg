using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

using Microsoft.Data.SqlClient;

using IntegrationTests.Support;

using Testcontainers.MsSql;

namespace IntegrationTests;

/// <summary>
/// Assembly fixture that owns the whole non-Aspire integration topology: a Testcontainers SQL Server,
/// schema built from scratch by the DatabaseMigrations tool, an in-process WireMock host, and the API +
/// Gateway launched as child <c>dotnet</c> processes wired per the proven boot recipe (handoff §5).
/// Once the Gateway is healthy it exports <c>PARITY_GATEWAY_URL</c> / <c>PARITY_SQL_CONNECTION</c> /
/// <c>PARITY_HEAPPE_MOCK_URL</c> so any collection-scoped <c>GatewayAttachFixture</c> enters attach mode
/// unchanged and performs the data seed/snapshot against this live topology.
/// </summary>
public sealed class TestcontainersTopologyFixture : IAsyncLifetime
{
    private const string DatabaseName = "UserOrgDBContext";

    // A syntactically valid but unreachable URL for the IdP/Waldur config the boot path eagerly parses
    // into URIs but never contacts under dev-auth.
    private const string DeadUrl = "http://127.0.0.1:1/dead";

    internal const string SealingKeyDocumentPath = "/.well-known/ogham-sealing-keys.json";

    // The proxy is released from its own repository and consumed as an image; keep this tag in step with
    // the one src/UserOrg.AppHost/AuditChainExtensions.cs pulls. Pulling it needs a `docker login` against
    // opencode.it4i.eu:5050 on whatever machine runs this suite.
    internal const string AuditProxyImage =
        "opencode.it4i.eu:5050/lexis-platform/backend/credential-service/audit/auditproxy/auditunsealingproxy:0.3.0";

    // Paths inside the proxy container. Absolute, because a relative one resolves against the container's
    // working directory, and under /tmp because the published image runs as a non-root user. Nothing here
    // outlives the run: the ring and the key store are per-run by design, and a key store that outlived
    // its ring would make the proxy refuse to bootstrap.
    internal const string ContainerKeyRingDirectory = "/tmp/audit/key-ring";
    internal const string ContainerSealingKeyStoreDirectory = "/tmp/audit/sealing-keys";

    internal const int AuditProxyContainerPort = 8080;

    private MsSqlContainer? _sqlContainer;
    private WireMockTopology? _wireMock;
    private IContainer? _auditProxy;
    private ChildProcess? _api;
    private ChildProcess? _gateway;
    private string _connectionString = string.Empty;
    private string _gatewayUrl = string.Empty;
    private string _auditProxyUrl = string.Empty;
    private string? _auditRoot;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "IDisposableAnalyzers.Correctness", "IDISP003:Dispose previous before re-assigning",
        Justification = "Fields are assigned exactly once during this one-shot lifecycle init and disposed in DisposeAsync.")]
    public async ValueTask InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var ct = cts.Token;

        try
        {
            var repoRoot = ResolveRepoRoot();
            var artifacts = ResolveArtifacts();

            // 1. SQL container, then the app database connection string (EF migrations create the DB).
            _sqlContainer = new MsSqlBuilder()
                .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _sqlContainer.StartAsync(ct);
            _connectionString = new SqlConnectionStringBuilder(_sqlContainer.GetConnectionString())
            {
                InitialCatalog = DatabaseName,
                TrustServerCertificate = true
            }.ConnectionString;

            // 2. Schema from scratch via the DatabaseMigrations tool (6 contexts) — reused as-is.
            await RunMigrationsAsync(artifacts.MigrationsDll, ct);
            await RestoreSeedCompatibilityDefaultsAsync(ct);

            // 3. In-process WireMock (HEAppE + Vault KV-v2 + EmailSender).
            _wireMock = WireMockTopology.Start(Path.Combine(repoRoot, "src", "UserOrg.AppHost", "heappe-mappings"));

            // 4. Unsealing proxy, before any producer: the producers below refuse to start without a
            // published sealing key. Its own directories live inside the container; the per-run root here
            // holds the producer-side spools and key caches, which belong to the host processes.
            _auditRoot = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"userorg-e2e-audit-{Guid.NewGuid():N}")).FullName;
            _auditProxy = BuildAuditProxyContainer();
            await _auditProxy.StartAsync(ct);
            _auditProxyUrl = $"http://127.0.0.1:{_auditProxy.GetMappedPublicPort(AuditProxyContainerPort)}";
            await WaitForPublishedSealingKeyAsync(ct);

            // 5. Child API + Gateway with the exact boot-recipe env.
            var apiPort = GetFreeTcpPort();
            var gatewayPort = GetFreeTcpPort();
            var apiUrl = $"http://127.0.0.1:{apiPort}";
            var gatewayUrl = $"http://127.0.0.1:{gatewayPort}";
            _gatewayUrl = gatewayUrl;

            _api = ChildProcess.Start(artifacts.ApiDll, ApiEnvironment(apiUrl, gatewayUrl, repoRoot));
            _gateway = ChildProcess.Start(artifacts.GatewayDll, GatewayEnvironment(gatewayUrl, apiUrl, repoRoot));

            // 6. Wait for the Gateway to become healthy (RBAC seed hosted service fills forge.Roles here).
            await WaitForGatewayAsync(gatewayUrl, ct);

            // 7. Export the attach-mode seam so a later GatewayAttachFixture reuses this topology unchanged.
            Environment.SetEnvironmentVariable("PARITY_GATEWAY_URL", gatewayUrl);
            Environment.SetEnvironmentVariable("PARITY_SQL_CONNECTION", _connectionString);
            Environment.SetEnvironmentVariable("PARITY_HEAPPE_MOCK_URL", _wireMock.Url);
        }
        catch
        {
            // A partway init failure must not orphan the container/child processes — xUnit's guarantee to
            // call DisposeAsync on a fixture whose init threw is runner-version-dependent. Tear down whatever
            // already started, then rethrow.
            await TeardownAsync();
            throw;
        }
    }

    /// <summary>The WireMock host, exposed so tests can swap the EmailSender response (B05).</summary>
    public WireMockTopology WireMock =>
        _wireMock ?? throw new InvalidOperationException("Topology has not been initialized.");

    /// <summary>
    /// Creates an unauthenticated Gateway HTTP client (no Authorization header), for tests that only
    /// need anonymous-tier access and have no reason to depend on the role-client-provisioning fixture.
    /// </summary>
    public HttpClient CreateAnonymousGatewayClient()
    {
        if (string.IsNullOrEmpty(_gatewayUrl))
            throw new InvalidOperationException("Topology has not been initialized.");

        return new HttpClient { BaseAddress = new Uri(_gatewayUrl) };
    }

    public ValueTask DisposeAsync() => TeardownAsync();

    // Idempotent teardown shared by DisposeAsync and the init-failure path — each resource is null-guarded
    // so it disposes only what actually started.
    private async ValueTask TeardownAsync()
    {
        _api?.Dispose();
        _gateway?.Dispose();
        _wireMock?.Dispose();

        if (_auditProxy is not null)
            await _auditProxy.DisposeAsync();

        if (_sqlContainer is not null)
            await _sqlContainer.DisposeAsync();

        // Nothing drains this spool — immugw is out of this topology — so a root left behind is a
        // directory of sealed envelopes no one will ever deliver, one per run.
        if (_auditRoot is not null && Directory.Exists(_auditRoot))
        {
            try
            {
                Directory.Delete(_auditRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A child that outlived its kill still holds these files open on Windows. This path also runs
                // after a failed init, where throwing would replace the init error with a cleanup one; a
                // leftover temp directory is the cheaper of the two.
            }
        }

        Environment.SetEnvironmentVariable("PARITY_GATEWAY_URL", null);
        Environment.SetEnvironmentVariable("PARITY_SQL_CONNECTION", null);
        Environment.SetEnvironmentVariable("PARITY_HEAPPE_MOCK_URL", null);
    }

    private Dictionary<string, string> CommonEnvironment(string moduleName) => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ConnectionStrings__UserOrgDBContext"] = _connectionString,
        ["ConnectionStrings__ForgeDBContext"] = _connectionString,
        ["ConnectionStrings__WorkflowDBContext"] = _connectionString,
        ["VaultCredentials__Address"] = _wireMock!.Url,
        ["VaultCredentials__MountPoint"] = "userorg",

        // AddServiceDefaults refuses to start a host without audit configuration, so the keys below are
        // as mandatory as the connection string. They point at the proxy container started above, over
        // plain HTTP: the compensating control ADR-0096 §4 demands is against an on-path attacker, and on
        // a published port of one test's own container on 127.0.0.1 there is none.
        ["EncryptedSpool__SpoolDirectory"] = AuditDirectory($"spool-{moduleName}"),
        ["EncryptedSpool__EndpointBaseUrl"] = _auditProxyUrl,
        ["EncryptedSpool__ModuleName"] = moduleName,
        ["OghamSealing__KeyEndpoint"] = $"{_auditProxyUrl}{SealingKeyDocumentPath}",
        ["OghamSealing__CacheDirectory"] = AuditDirectory($"key-cache-{moduleName}"),
        // A producer that reaches no key must announce it by refusing to start, rather than serve
        // traffic whose fail-closed call sites all answer 500 for a reason nothing states.
        ["OghamSealing__RequireKeyAtStartup"] = "true",
        ["OghamSealing__StartupKeyTimeout"] = "00:00:30",
        ["ImmuDb__Username"] = "e2e-tests",
        ["ImmuDb__Password"] = "e2e-tests",
        ["ImmuDb__Database"] = "e2e-tests",
    };

    // File mode keeps the key ring on disk instead of in Vault, which this topology has no instance of.
    // Ring and sealing key store share the container's filesystem by design: a key store that outlived its
    // ring makes the proxy refuse to bootstrap, rather than mint a fresh ring nothing sealed earlier can be
    // read under. A fresh container each run gives both, with no mount to keep in step.
    private static IContainer BuildAuditProxyContainer() => new ContainerBuilder()
        .WithImage(AuditProxyImage)
        .WithPortBinding(AuditProxyContainerPort, assignRandomHostPort: true)
        .WithEnvironment("VaultKeyRing__Mode", "File")
        .WithEnvironment("VaultKeyRing__Directory", ContainerKeyRingDirectory)
        .WithEnvironment("SealingKeyStoreDirectory", ContainerSealingKeyStoreDirectory)
        // immugw is out of this topology, so the drain worker retries into a dead URL for the life of
        // the run. Nothing here asserts on delivery — only on the sealing half being reachable at all.
        // 127.0.0.1 is the container itself here, where port 1 is just as dead.
        .WithEnvironment("ImmuGwBaseUrl", DeadUrl)
        .Build();

    private string AuditDirectory(string name) => Path.Combine(_auditRoot!, name);

    private Dictionary<string, string> ApiEnvironment(string apiUrl, string gatewayUrl, string repoRoot)
    {
        var env = new Dictionary<string, string>(CommonEnvironment("UserOrg.Api"))
        {
            ["ASPNETCORE_URLS"] = apiUrl,
            ["VaultCredentials__AddressPrefix"] = string.Empty,
            // Runtime HEAppE-credential client resolves the "vault" service via service discovery.
            ["Services__vault__http__0"] = _wireMock!.Url,
            // Token exchange flag off: every HEAppE call goes through the NexusApi proxy; the WireMock stands in
            // for the proxied /api/heappe/{identifier}/ paths.
            ["TokenExchange__Nexus__BaseUrl"] = _wireMock.Url,
            ["DataAccessConfiguration__UserOrgDataSeedPath"] = Path.Combine(repoRoot, "Seeds", "UserOrgDataSeed.json"),
            ["AuthenticationConfiguration__MetadataAddress"] = DeadUrl,
            ["Idp__Authority"] = DeadUrl,
            ["Idp__Audience"] = "account",
            ["MailConfiguration__MailSenderUrl"] = _wireMock.Url,
            ["MailConfiguration__SenderEmail"] = "noreply@test.local",
            // API validates the internal JWT the Gateway mints against the Gateway's JWKS. In Development
            // RequireHttpsMetadata is false (AuthenticationExtensions), so HTTP metadata is accepted.
            ["GatewayInternal__Authority"] = gatewayUrl,
            ["GatewayInternal__Audience"] = "gateway-internal",
            ["Waldur__ApiBaseAddress"] = DeadUrl,
            ["Waldur__Enabled"] = "false",
            ["Heappe__ClusterAuthTypes__AllowedAuthenticationTypeNames__0"] = "SshCertificate",
            ["Pagination__DefaultLimit"] = "100",
            ["Pagination__MaxLimit"] = "300",
            ["Database__AutoMigrateOnStartup"] = "false",
            ["FeatureManagement__V2Api"] = "true",
        };
        return env;
    }

    private Dictionary<string, string> GatewayEnvironment(string gatewayUrl, string apiUrl, string repoRoot)
    {
        var env = new Dictionary<string, string>(CommonEnvironment("UserOrg.Gateway"))
        {
            ["ASPNETCORE_URLS"] = gatewayUrl,
            ["DevAuthOverride__Enabled"] = "true",
            ["RoleManagement__AccessToken"] = "test-token",
            // Directly on the Gateway process (no AppHost in the loop) — research/rbac-seed.md.
            ["RoleManagement__SeedFilePath"] = Path.Combine(repoRoot, "roleManagmentRBAC.json"),
            ["InternalToken__Issuer"] = gatewayUrl,
            ["Services__userorg__http__0"] = apiUrl,
            ["Idp__Authority"] = DeadUrl,
            ["Idp__Audience"] = "account",
            ["Idp__Introspection__Enabled"] = "false",
        };
        return env;
    }

    private async Task RunMigrationsAsync(string migrationsDll, CancellationToken ct)
    {
        var env = new Dictionary<string, string>
        {
            ["ConnectionStrings__UserOrgDBContext"] = _connectionString,
            ["ConnectionStrings__ForgeDBContext"] = _connectionString,
            ["ConnectionStrings__WorkflowDBContext"] = _connectionString,
        };

        var (exitCode, output) = await RunToCompletionAsync("dotnet", $"\"{migrationsDll}\" apply", env, ct);
        if (exitCode != 0)
            throw new InvalidOperationException($"DatabaseMigrations apply failed (exit {exitCode}):{Environment.NewLine}{output}");
    }

    // The shadow-Id refactor moved Guid PK generation from a SQL-side DEFAULT NewId() to a client-side
    // EF value generator (project_modular_shadow_id_generation_gap). The reused attach-mode seeding
    // (GatewayAttachFixture + seed-data.sql) still inserts several rows via raw SQL WITHOUT an Id — it
    // was written against that DEFAULT. A migrations-from-scratch DB has no such default, so those
    // inserts fail with "Cannot insert NULL into 'Id'". Restore a NEWID() default on every dbo Id column
    // that lacks one; the client-side generator still supplies values on the app path, so the default
    // only ever fires for these raw seed inserts.
    private async Task RestoreSeedCompatibilityDefaultsAsync(CancellationToken ct)
    {
        const string sql = """
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
                + N' ADD CONSTRAINT ' + QUOTENAME(N'DF_' + t.name + N'_Id_seedcompat')
                + N' DEFAULT NEWID() FOR ' + QUOTENAME(c.name) + N';'
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE ty.name = 'uniqueidentifier'
              AND c.name = 'Id'
              AND c.is_nullable = 0
              AND c.default_object_id = 0
              AND s.name = 'dbo';
            EXEC sp_executesql @sql;
            """;

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    // The proxy answers its key document long before the rotation service has minted anything into it,
    // so an empty "keys" array is the state a producer must never be started against.
    private async Task WaitForPublishedSealingKeyAsync(CancellationToken ct)
    {
        using var client = new HttpClient { BaseAddress = new Uri(_auditProxyUrl), Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(2);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (_auditProxy!.State is not TestcontainersStates.Running)
                throw new InvalidOperationException($"The audit unsealing proxy stopped before publishing a sealing key.{ChildDiagnostics()}");

            try
            {
                using var response = await client.GetAsync(new Uri(SealingKeyDocumentPath, UriKind.Relative), ct);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (document.RootElement.TryGetProperty("keys", out var keys) && keys.GetArrayLength() > 0)
                        return;
                }
            }
            catch (HttpRequestException)
            {
                // Proxy not accepting connections yet — keep polling.
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        throw new InvalidOperationException(
            $"The audit unsealing proxy published no sealing key at {_auditProxyUrl}{SealingKeyDocumentPath}.{ChildDiagnostics()}");
    }

    private async Task WaitForGatewayAsync(string gatewayUrl, CancellationToken ct)
    {
        using var client = new HttpClient { BaseAddress = new Uri(gatewayUrl), Timeout = TimeSpan.FromSeconds(10) };

        if (!await GatewayReadiness.WaitAsync(client, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(2), EnsureChildrenAlive, ct))
        {
            throw new InvalidOperationException(
                $"Gateway did not become ready at {gatewayUrl}: /api/version answered and {GatewayReadiness.UnservedPath} got the route-existence 404.{ChildDiagnostics()}");
        }
    }

    private void EnsureChildrenAlive()
    {
        if (_auditProxy!.State is not TestcontainersStates.Running || _api!.HasExited || _gateway!.HasExited)
            throw new InvalidOperationException($"A topology member stopped before the Gateway became healthy.{ChildDiagnostics()}");
    }

    // With RequireKeyAtStartup a mis-wired audit chain kills the API and the Gateway at startup, which
    // otherwise reads exactly like this machine's pre-existing container failure — hence the proxy's own
    // stream, labelled alongside theirs.
    private string ChildDiagnostics() =>
        $"{Environment.NewLine}--- audit proxy output ---{Environment.NewLine}{AuditProxyLogs()}{Environment.NewLine}--- API output ---{Environment.NewLine}{_api?.Output}{Environment.NewLine}--- Gateway output ---{Environment.NewLine}{_gateway?.Output}";

    // Blocking, because the callers are exception-message builders on failure paths and the alternative is
    // making them async only to read a log. A container that never started has no logs to read, which must
    // not replace the failure being reported with one about fetching its diagnostics.
    private string AuditProxyLogs()
    {
        try
        {
            var (stdout, stderr) = _auditProxy!.GetLogsAsync().GetAwaiter().GetResult();
            return stdout + stderr;
        }
        catch (Exception ex)
        {
            return $"(container logs unavailable: {ex.Message})";
        }
    }

    private static (string ApiDll, string GatewayDll, string MigrationsDll) ResolveArtifacts()
    {
        // BaseDirectory: <repo>/artifacts/bin/IntegrationTests/<config>/ — the sibling projects share the
        // same artifacts/bin/<Project>/<config> layout (UseArtifactsOutput).
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        var config = baseDir.Name;
        var binDir = baseDir.Parent?.Parent
            ?? throw new InvalidOperationException($"Unexpected artifacts layout at {AppContext.BaseDirectory}.");

        string Dll(string project) => Path.Combine(binDir.FullName, project, config, $"{project}.dll");

        var api = Dll("API");
        var gateway = Dll("UserOrg.Gateway");
        var migrations = Dll("DatabaseMigrations");

        foreach (var dll in new[] { api, gateway, migrations })
        {
            if (!File.Exists(dll))
                throw new FileNotFoundException($"Expected built assembly not found — build the solution first.", dll);
        }

        return (api, gateway, migrations);
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "UserOrganization.slnx")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root (UserOrganization.slnx / .git) could not be located.");
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<(int ExitCode, string Output)> RunToCompletionAsync(
        string fileName,
        string arguments,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var (key, value) in environment)
            process.StartInfo.Environment[key] = value;

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        lock (output)
            return (process.ExitCode, output.ToString());
    }
}
