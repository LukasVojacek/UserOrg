using Microsoft.Extensions.Configuration;

using UserOrg.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("compose");

/// Neither the database source nor the identity provider is decided here (T-071 E1): outside the Testing
/// environment both are answered in one dialog raised after Build() and read at resource-start time. These two flags carry
/// only the Testing environment's "force the local ones, do not ask".
bool useLocalConnections;

bool useLocalAuthority;

if (builder.Environment.EnvironmentName == "Testing")
{
    useLocalConnections = true;
    useLocalAuthority = true;
}
else
{
    useLocalConnections = false;
    useLocalAuthority = false;
}

var runSources = new RunSourcesDialog(forceLocalContainer: useLocalConnections, forceLocalKeycloak: useLocalAuthority);
var databaseSource = new DatabaseSourceSelection(runSources);
var identityProvider = new IdentityProviderSelection(runSources);
////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

var parameters = builder.AddParameters();

// Every producer exports OTLP here and the collector forwards to the dashboard, so the dashboard keeps
// showing the same traces while the local topology carries the collector a deployed one needs.
var otelCollector = builder.AddOtelCollector();

var (immugw, auditLogTable) = builder.AddImmuDb();
var (vaultProxy, vaultSetup) = builder.AddHashiVault();
// "UserOrg.Api", not the host assembly's own name "API": the archive is append-only, so the
// string chosen here is the one every row this host ever writes will carry.
const string apiModuleName = "UserOrg.Api";
const string gatewayModuleName = "UserOrg.Gateway";
var auditProxy = builder.AddAuditUnsealingProxy(immugw, auditLogTable);
var emailsender = builder.AddEmailSender(parameters);

// Waldur.Sync is pull-based (ADR-0056): the STOMP/websocket transport is gone, so no STOMP server
// or waldurWss external service is wired.
var api = builder.AddProject<Projects.API>("userorg")
    .WithIconName("DatabasePerson")
    .WaitFor(emailsender)
    .WithApiEnvironment(parameters, emailsender.GetEndpoint("http"), vaultProxy.GetEndpoint("http"))
    .WithAuditChainEnvironment(auditProxy, apiModuleName)
    .WithWaldurEnvironment(parameters)
    // Configurable v2 list paging bounds (ADR-0020): default page size 100, hard cap 300.
    .WithEnvironment("Pagination__DefaultLimit", "100")
    .WithEnvironment("Pagination__MaxLimit", "300")
    .WithInactiveUserSyncEnvironment(parameters)
    .WithOtelCollectorEnvironment(otelCollector)
    .WithUrlForEndpoint("https", static url =>
    {
        url.DisplayText = "🧩 Core API";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });

// WithBrowserLogs is experimental and gated behind ASPIREBROWSERLOGS001.
#pragma warning disable ASPIREBROWSERLOGS001
var gateway = builder.AddProject<Projects.UserOrg_Gateway>("gateway")
    .WithIconName("Door")
    .WithGatewayEnvironment(parameters, vaultProxy.GetEndpoint("http"))
    .WithAuditChainEnvironment(auditProxy, gatewayModuleName)
    .WithReference(api)
    .WithOtelCollectorEnvironment(otelCollector)
    .WithExternalHttpEndpoints()
    // The API explorer and everything behind it is served from this resource, so the browser console is
    // where its failures show up first - a request the explorer never sends leaves no trace in any log
    // this app host already collects. Experimental, hence the suppression.
    .WithBrowserLogs()
    .WithUrlForEndpoint("https", static url =>
    {
        url.DisplayText = "🚪 Gateway";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    })
    .WithUrls(async context =>
    {
        var httpsUrl = context.Urls.FirstOrDefault(u => u.Endpoint?.EndpointName == "https")?.Url;

        if (!string.IsNullOrEmpty(httpsUrl))
        {
            context.Urls.Add(new ResourceUrlAnnotation
            {
                Url = $"{httpsUrl}/api",
                DisplayText = "🙏 UserOrg",
                DisplayLocation = UrlDisplayLocation.SummaryAndDetails
            });
#pragma warning restore ASPIREBROWSERLOGS001

            var tokenValue = await parameters.RoleManagementAccessToken.Resource.GetValueAsync(context.CancellationToken);
            context.Urls.Add(new ResourceUrlAnnotation
            {
                Url = $"{httpsUrl}/role-management?t={tokenValue}",
                DisplayText = "🛠️ Forge management",
                DisplayLocation = UrlDisplayLocation.SummaryAndDetails
            });

            context.Urls.Add(new ResourceUrlAnnotation
            {
                Url = $"{httpsUrl}/.well-known/openid-configuration",
                DisplayText = "⛩️ OpenID configuration",
                DisplayLocation = UrlDisplayLocation.DetailsOnly
            });
        }
    });

api.WithEnvironment("GatewayInternal__Authority", gateway.GetEndpoint("https"));

gateway.WithEnvironment("InternalToken__Issuer", gateway.GetEndpoint("https"));

// Develop-only auth override (ADR-0045). Wired ONLY in run mode, so this parameter and its env
// wiring never reach a published manifest. The Gateway additionally registers the override scheme
// only under IsDevelopment(), and only acts on a request when this flag is enabled.
if (builder.ExecutionContext.IsRunMode)
{
    var devAuthOverrideEnabled = builder.AddParameter("DevAuthOverrideEnabled")
        .WithCustomInput(_ => new()
        {
            Name = "Dev auth override — enabled",
            Description = "Develop only: skip the IdP and treat the Bearer string itself as the caller's idpid (impersonate that user). Off = real IdP tokens flow normally.",
            InputType = InputType.Boolean,
            Value = "false"
        });

    gateway.WithEnvironment("DevAuthOverride__Enabled", devAuthOverrideEnabled);
}

// HEAppE HTTP fake (ADR-0051). On by default under `aspire run`: without it the seed writes its
// own non-mock endpoint into AssignedLocationValue HEAPPE_URL, that host does not exist, and every
// HEAppE-backed path answers 503 off the liveness probe — TemplatesInfo among them — so the seeded
// projects cannot be exercised at all. --Parameters:UseHeappeMock=false turns it off for a run that
// wants to reach a real HEAppE. Never in publish mode: it is excluded from any manifest either way.
// The Core API reaches it purely via seed data, so no service reference/env wiring is needed.
IResourceBuilder<WireMockServerResource>? heappeMock = null;
if (builder.Configuration.GetValue("Parameters:UseHeappeMock", defaultValue: builder.ExecutionContext.IsRunMode))
{
    heappeMock = builder.AddHeappeMock();
}

// HEAppE is reached only through NexusApi (#349). This follows the mock because the local NexusApi's registry
// routes the heappe-mock instance to it. Which NexusApi is a dialog answered after Build(), like the database source.
var nexusApi = new NexusApiSelection(runSources);
var nexusApiContainer = api.WithNexusApi(builder, heappeMock, nexusApi, parameters.FeatureTokenExchangeEnabled);

// Both identity providers are in the model whatever the choice turns out to be: the choice is only known
// once the dashboard dialog is answered, which happens after Build() (T-071 E1). Keycloak is explicit-start
// and IdentityProviderSelection starts it when the answer is LocalKeycloak.
var (keycloak, keycloakUsername, keycloakPassword) = builder.AddKeycloakService();
keycloak
    .WithClientOrigin(KeycloakExtensions.GatewayOriginVariable, gateway)
    .WithClientOrigin(KeycloakExtensions.ApiOriginVariable, api);
var externalIdentityProvider = parameters.ExternalIdentityProvider;

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithEnvironment("TokenExchange__ClientId", externalIdentityProvider.TokenExchangeClientId)
        .WithEnvironment("TokenExchange__ClientSecret", externalIdentityProvider.TokenExchangeClientSecret)
        .WithEnvironment("TokenExchange__Authority", externalIdentityProvider.Authority);
}
else
{
    Task<TokenExchangeClient>? tokenExchangeClient = null;

    api.WithEnvironment(async context =>
    {
        var tokenExchangeEnabled = NexusApiModes.ParseTokenExchangeEnabled(
            await parameters.FeatureTokenExchangeEnabled.Resource.GetValueAsync(context.CancellationToken));

        if (tokenExchangeEnabled && !await identityProvider.UseLocalKeycloakAsync(context.CancellationToken))
        {
            if (tokenExchangeClient is null || tokenExchangeClient.IsFaulted || tokenExchangeClient.IsCanceled)
            {
                var idpAuthority = await externalIdentityProvider.Authority.Resource.UrlParameter!.GetValueAsync(context.CancellationToken);
                tokenExchangeClient = TokenExchangePrompt.ResolveAsync(context.ExecutionContext.ServiceProvider, idpAuthority, context.CancellationToken);
            }

            var client = await tokenExchangeClient;
            context.EnvironmentVariables["TokenExchange__ClientId"] = client.TokenExchangeClientId;
            context.EnvironmentVariables["TokenExchange__ClientSecret"] = client.TokenExchangeClientSecret;
            context.EnvironmentVariables["TokenExchange__Authority"] = client.Authority;
        }
    });
}

api.WithEnvironment("Idp__Authority", externalIdentityProvider.Authority)
    .WithEnvironment("Idp__Audience", externalIdentityProvider.Audience)
    // The discovery document, not the authority - the same address the local Keycloak override below
    // builds. Trimmed rather than concatenated so the shape of an IdpAuthority override decides
    // nothing: .../auth/realms/LEXIS_AAI_v2_DEV/.well-known/openid-configuration.
    .WithEnvironment(async context =>
    {
        var authority = await externalIdentityProvider.Authority.Resource.UrlParameter!
            .GetValueAsync(context.CancellationToken);

        context.EnvironmentVariables["AuthenticationConfiguration__MetadataAddress"] =
            $"{authority?.TrimEnd('/')}/.well-known/openid-configuration";
    });

gateway
    .WithEnvironment("Idp__Authority", externalIdentityProvider.Authority)
    .WithEnvironment("Idp__Audience", externalIdentityProvider.Audience)
    .WithEnvironment("Idp__AdditionalAudiences", externalIdentityProvider.AdditionalAudiences)
    .WithEnvironment("Idp__Introspection__Enabled", externalIdentityProvider.EnableIntrospection)
    .WithEnvironment("Idp__Introspection__ClientId", externalIdentityProvider.IntrospectionClientId)
    .WithEnvironment("Idp__Introspection__ClientSecret", externalIdentityProvider.IntrospectionClientSecret)
    .WithEnvironment("Idp__Introspection__Endpoint", externalIdentityProvider.IntrospectionEndpoint);

// The local container overwrites those values, and only then: an environment callback runs at resource
// start, which is the earliest moment at which the answer exists, and it runs after the assignments above
// because it is registered after them. Awaiting the answer here is also what orders the two projects behind
// a healthy Keycloak, which the removed `gateway.WaitFor(keycloak)` edge used to do.
var localKeycloakAuthority = keycloak.RealmAuthority();
var localKeycloakMetadataAddress = keycloak.RealmMetadataAddress();
var localKeycloakAuthorizationUrl = keycloak.RealmAuthorizationUrl();
var localKeycloakTokenUrl = keycloak.RealmTokenUrl();

api.WithEnvironment(async context =>
{
    if (!await identityProvider.UseLocalKeycloakAsync(context.CancellationToken))
    {
        return;
    }

    context.EnvironmentVariables["Idp__Authority"] = localKeycloakAuthority;
    context.EnvironmentVariables["Idp__Audience"] = "master-realm";
    context.EnvironmentVariables["AuthenticationConfiguration__MetadataAddress"] = localKeycloakMetadataAddress;
    context.EnvironmentVariables["Scalar__Oidc__AuthorizationUrl"] = localKeycloakAuthorizationUrl;
    context.EnvironmentVariables["Scalar__Oidc__TokenUrl"] = localKeycloakTokenUrl;
    context.EnvironmentVariables["Scalar__Oidc__ClientId"] = KeycloakExtensions.ScalarClientId;

    var tokenExchangeEnabled = NexusApiModes.ParseTokenExchangeEnabled(
        await parameters.FeatureTokenExchangeEnabled.Resource.GetValueAsync(context.CancellationToken));

    if (tokenExchangeEnabled)
    {
        context.EnvironmentVariables["TokenExchange__ClientId"] = "testClient";
        context.EnvironmentVariables["TokenExchange__ClientSecret"] = "testClient";
        context.EnvironmentVariables["TokenExchange__Authority"] = localKeycloakAuthority;
    }
});

gateway.WithEnvironment(async context =>
{
    if (!await identityProvider.UseLocalKeycloakAsync(context.CancellationToken))
    {
        return;
    }

    context.EnvironmentVariables["Idp__Authority"] = localKeycloakAuthority;
    context.EnvironmentVariables["Idp__Audience"] = "master-realm";
    context.EnvironmentVariables["Idp__AdditionalAudiences"] = $"{KeycloakExtensions.TestClientId},account";
    context.EnvironmentVariables["Idp__Introspection__Enabled"] = "false";
    context.EnvironmentVariables["Idp__Introspection__ClientId"] = KeycloakExtensions.TestClientId;
    context.EnvironmentVariables["Idp__Introspection__ClientSecret"] = KeycloakExtensions.TestClientSecret;
    // Idp__Introspection__Endpoint is deliberately left on its parameter: introspection is off on this
    // branch, so the value is never read, and overwriting it would put a second assignment on a variable
    // that has to keep coming from IntrospectionEndpoint alone.
});

gateway.WithGetTokenCommand(keycloak, keycloakUsername, keycloakPassword, identityProvider);
keycloak.WithLoginsCommand(keycloakUsername, keycloakPassword);

// Everything the local database needs is in the model whatever the DatabaseSource choice turns out to be:
// the choice is only known once the dashboard dialog is answered, which happens after Build(). The chain is
// explicit-start and DatabaseSourceSelection starts it when the answer is LocalContainer (T-071 E1).
// Neither parameter is Required: on an external connection string there is no backup to restore, and the
// unresolved-parameter dialog must not demand a path nobody has.
var databaseBackupFilePath = builder.AddParameter("DatabaseBackupFilePath", secret: false)
    .WithCustomInput(parameter => new()
    {
        Name = "Database Backup File Path",
        Description = "Full path to the SQL Server backup file (.bak). You can specify an absolute path (e.g., C:\\backups\\userorg-test-backup.bak) or a relative path. Do not copy the backup file to a specific location — provide the path to your existing backup file. Only used by the local SQL container.",
        InputType = InputType.Text,
        Required = false,
        Value = "./dbbackup/userorg-test-backup-2026-03-10.bak",
        Placeholder = "./dbbackup/userorg-test-backup-2026-03-10.bak"
    });
var databaseName = builder.AddParameter("DatabaseName", secret: false)
    .WithCustomInput(parameter => new()
    {
        Name = "Database Logical Name",
        Description = "The logical file name of the database inside the backup file. This is the name used in the MOVE clause during restore. You can find this by examining the backup file contents. Only used by the local SQL container.",
        InputType = InputType.Text,
        Required = false,
        Value = "userorg_v2",
        Placeholder = "userorg_v2"
    });

// E2E tests pass --Parameters:EphemeralContainers=true so the SQL container is torn down with the
// app host (Session) instead of persisting across runs — otherwise it survives and carries stale
// test state between runs (the source of flaky "extra" failures). The infra containers (Vault,
// immudb, ...) stay Persistent even in tests: they hold no per-test state and are slow/fragile to
// recreate every run. Local `aspire run` keeps Persistent here to avoid re-restoring the DB.
var ephemeralContainers = builder.Configuration.GetValue("Parameters:EphemeralContainers", defaultValue: false);
var sqlLifetime = ephemeralContainers
    ? ContainerLifetime.Session
    : ContainerLifetime.Persistent;
var sql = builder.AddSqlServer("userorgDatabase", password: parameters.DatabasePassword)
    .WithLifetime(sqlLifetime)
    .WithDatabaseRestoreCommand(
        databaseBackupFilePath,
        databaseName,
        "UserOrgDBContext")
    .WithSnapshotCommands("UserOrgDBContext")
    .WithImage("mssql/server", "2025-latest")
    .WithExplicitStart()
    .WithUrlForEndpoint("tcp", static url =>
    {
        url.DisplayText = "🗄️ SQL Server";
        url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
    });

// DbGate is a DB-admin UI for local `aspire run` — not needed during E2E test runs (EphemeralContainers),
// where it's just an extra container to spin up and clean up. It follows the SQL container, not the
// database source: it is started along with the chain, and stays stopped on an external connection string.
IResourceBuilder<DbGateContainerResource>? dbGate = null;
if (!ephemeralContainers)
{
    sql.WithDbGate(container => dbGate = container
        .WithExplicitStart()
        .WithUrlForEndpoint("http", static url =>
        {
            url.DisplayText = "🔍 DbGate";
            url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
        }));
}

var db = sql.AddDatabase("UserOrgDBContext")
    .WithParentRelationship(sql);

// The connection strings and the Vault address are the only things the choice decides, and both are
// written from a resource-start callback — the earliest moment at which the answer exists.
api.WithEnvironment(async context =>
{
    var useLocalContainer = await databaseSource.UseLocalContainerAsync(context.CancellationToken);
    object connectionString = useLocalContainer
        ? new ConnectionStringReference(db.Resource, optional: false)
        : parameters.UserorgDev.Resource;

    context.EnvironmentVariables["ConnectionStrings__ForgeDBContext"] = connectionString;
    context.EnvironmentVariables["ConnectionStrings__UserOrgDBContext"] = connectionString;
    context.EnvironmentVariables["ConnectionStrings__WorkflowDBContext"] = connectionString;

    // Always the local Vault proxy, whatever the database source turns out to be: no deployment of
    // this app host reaches a remote Vault, so there is no second branch to keep alive.
    context.EnvironmentVariables["VaultCredentials__Address"] = vaultProxy.GetEndpoint("http");
    context.EnvironmentVariables["VaultCredentials__AddressPrefix"] = "";
    context.EnvironmentVariables["VaultCredentials__MountPoint"] = "userorg";
});

// No edge to the gateway: nothing the API needs at boot comes from there. GatewayInternal__Authority
// only feeds JwtBearerOptions.Authority, which is fetched lazily at the first token validation, and
// the gateway's own health check calls the API - the dependency runs gateway -> api, not api -> gateway.
api.WaitFor(vaultProxy);

gateway.WithEnvironment(async context =>
{
    var useLocalContainer = await databaseSource.UseLocalContainerAsync(context.CancellationToken);
    object connectionString = useLocalContainer
        ? new ConnectionStringReference(db.Resource, optional: false)
        : parameters.UserorgDev.Resource;

    context.EnvironmentVariables["ConnectionStrings__ForgeDBContext"] = connectionString;
    context.EnvironmentVariables["ConnectionStrings__UserOrgDBContext"] = connectionString;
    context.EnvironmentVariables["ConnectionStrings__WorkflowDBContext"] = connectionString;
});

var migrationApply = builder.AddMigrationTools(sql, db);
// Migrations must FINISH before the gateway and API start: the gateway's
// ForgeMigrationStartupService throws on pending migrations. WaitFor only waits
// for the migration executable to *start* (be Running); WaitForCompletion waits
// for it to run to completion, which is what "start only after migrations applied"
// actually requires. Using WaitFor here is the cause of the gateway crash.
// On an external connection string db-migrations-apply never runs and DatabaseSourceSelection reports it
// as finished instead, so these two edges release without migrating a database this host does not own.
api.WaitForCompletion(migrationApply);
gateway.WaitForCompletion(migrationApply);

var seedTool = builder.AddSeedTool(migrationApply, gateway, db, heappeMock);

sql.WithDatabaseLifecycleCommand("UserOrgDBContext", migrationApply, seedTool);

var k6BearerToken = builder.AddParameter("K6BearerToken", new RuntimeDefault(""), secret: true)
    .WithCustomInput(parameter => new()
    {
        Name = "k6 Bearer Token",
        Description = "Paste the JWT access token for k6 load tests. Obtain it by running the 'gettoken' command on the gateway resource.",
        InputType = InputType.SecretText,
        Required = false,
        Placeholder = "eyJ..."
    });

builder.AddK6("k6")
    .WithBindMount("k6", "/scripts", isReadOnly: true)
    .WithExplicitStart()
    .WithScript("/scripts/userinfo-load-test.js")
    .WithReference(gateway)                          // injects services__gateway__https__0
    .WaitFor(gateway)
    // secret parameter entered in the Aspire dashboard
    .WithEnvironment("BEARER_TOKEN", k6BearerToken)
    // A load-test driver, not part of the topology being deployed, and the only thing in the model that
    // bind-mounts the repository tree: published it becomes a service whose scripts directory is a path
    // on whoever ran `aspire publish`.
    .ExcludeFromManifest();

var app = builder.Build();

// After Build(), because that is when the dashboard can answer the DatabaseSource dialog. Not awaited:
// the answer arrives while the host is already running.
databaseSource.ApplyTo(app, parameters, sql, migrationApply, dbGate, seedTool, db);

// Same reason, for the identity provider the dialog decides.
identityProvider.ApplyTo(app, keycloak);

// Same reason, for the NexusApi the dialog decides.
nexusApi.ApplyTo(app, nexusApiContainer, parameters.FeatureTokenExchangeEnabled.Resource);

// Same reason, and the VaultProxy's cold-start guarantee: the AppRole credentials are issued over HTTP
// and written to disk here, and only then is the explicit-start proxy started.
vaultSetup.ApplyTo(app);

await app.RunAsync();
