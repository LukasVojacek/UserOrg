namespace UserOrg.AppHost;

internal static class IDistributedApplicationBuilderExtensions
{
    internal sealed record ExternalIdentityProviderParameters(
        IResourceBuilder<ExternalServiceResource> Authority,
        IResourceBuilder<ParameterResource> Audience,
        IResourceBuilder<ParameterResource> AdditionalAudiences,
        IResourceBuilder<ParameterResource> IntrospectionClientId,
        IResourceBuilder<ParameterResource> IntrospectionClientSecret,
        IResourceBuilder<ParameterResource> IntrospectionEndpoint,
        IResourceBuilder<ParameterResource> EnableIntrospection,
        IResourceBuilder<ParameterResource> TokenExchangeClientId,
        IResourceBuilder<ParameterResource> TokenExchangeClientSecret
    );

    internal sealed record Parameters(
        IResourceBuilder<ParameterResource> UserorgDev,
        IResourceBuilder<ExternalServiceResource> RemoteDatabaseHost,
        IResourceBuilder<ParameterResource> DatabasePassword,
        IResourceBuilder<ExternalServiceResource> CyclopsBaseUrl,
        IResourceBuilder<ParameterResource> CyclopsTimeUsageRelativeUrl,
        IResourceBuilder<ParameterResource> CyclopsTotalUsageRelativeUrl,
        IResourceBuilder<ParameterResource> EnableWaldurSyncIntegration,
        IResourceBuilder<ExternalServiceResource> WaldurBaseUrl,
        IResourceBuilder<ParameterResource> WaldurSyncIntegrationPeriod,
        IResourceBuilder<ParameterResource> RoleManagementAccessToken,
        ExternalIdentityProviderParameters ExternalIdentityProvider,
        IResourceBuilder<ParameterResource>? ScalarServerUrl,
        IResourceBuilder<ParameterResource> GatewayInternalAudience,
        IResourceBuilder<ParameterResource> FeatureReadOnlyMode,
        IResourceBuilder<ParameterResource> FeatureV1Api,
        IResourceBuilder<ParameterResource> FeatureV2Api,
        IResourceBuilder<ParameterResource> FeatureTokenExchangeEnabled,
        IResourceBuilder<ParameterResource> FeatureStrictJwtAudience,
        IResourceBuilder<ParameterResource> AutoMigrateOnStartup,
        IResourceBuilder<ParameterResource> SerilogBaseLevel,
        IResourceBuilder<ParameterResource> SerilogStepUpLevel,
        IResourceBuilder<ParameterResource> SerilogCaptureRequestBody,
        IResourceBuilder<ParameterResource> SerilogAlwaysLogRequestSummary,
        IResourceBuilder<ParameterResource> SerilogMode,
        IResourceBuilder<ParameterResource> SerilogEnableConsoleLogging,
        IResourceBuilder<ParameterResource> GatewayRoleManagementSeedFilePath,
        IResourceBuilder<ParameterResource> EnableInactiveUserSync,
        IResourceBuilder<ParameterResource> InactiveUserSyncBaseUrl,
        IResourceBuilder<ParameterResource> InactiveUserSyncClientId,
        IResourceBuilder<ParameterResource> InactiveUserSyncClientSecret,
        IResourceBuilder<ParameterResource> InactiveUserSyncSyncInterval,
        IResourceBuilder<ParameterResource> EmailSenderApiKey,
        IResourceBuilder<ParameterResource> EmailSenderBrowserToken
    );

    extension(IDistributedApplicationBuilder builder)
    {
        public Parameters AddParameters()
        {
            // Both database parameters are registered whatever the run turns out to use: the DatabaseSource
            // choice is answered after the model is built (T-071 E1). Neither is Required — a run on the
            // local container has no external connection string, and vice versa.
            var userorgDev = builder.AddParameter("UserorgDev", secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "External Database Connection String",
                    Description = "The connection string used when the database source is an external database. Leave empty when running against the local SQL container.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Value = "Server=sql.dev.example.com,1433;Database=userorg;User Id=userorg;Password=;TrustServerCertificate=True",
                    Placeholder = "Server=…;Database=…;User Id=…;Password=…"
                });

            var remoteDatabaseHost = builder.AddParameter("RemoteDatabaseHost", new RuntimeDefault("tcp://sql.dev.example.com:1433"), secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Remote Database Host",
                    Description = "The URL the remote-database resource shows until the database source is answered. An external database replaces it with the host in the UserorgDev connection string. Graph visibility only: it feeds no connection string.",
                    InputType = InputType.Text,
                    Required = false,
                    Placeholder = "e.g. tcp://sql.dev.example.com:1433"
                });
            var remoteDatabase = builder.AddExternalService("remote-database", remoteDatabaseHost)
                .WithDetailsOnlyUrls("🗃️ Remote database");

            // Registered whatever the identity-provider choice turns out to be (T-071 E1): the choice is a
            // dialog answered after Build(), so it cannot decide which parameters the model carries.
            var idpAuthority = builder.AddParameter("IdpAuthority", new RuntimeDefault("https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/"), secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Identity Provider Authority URL",
                    Description = "The authority URL of the identity provider.",
                    InputType = InputType.Text,
                    Required = true,
                    Placeholder = "e.g. https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV"
                });

            // Relative, against an authority default that ends in a slash: a leading-slash path
            // replaces the realm path and probes the host root, which 404s. The document this has
            // to reach is .../auth/realms/LEXIS_AAI_v2_DEV/.well-known/openid-configuration.
            // Not "idp": resource names are case-insensitive and the Keycloak container is already `Idp`.
            // Both are in the model now that the identity provider is chosen at run time, so the two names
            // meet, and the app host refuses to build when they collide.
            var idpAuthorityService = builder.AddExternalService("idp-authority", idpAuthority)
                .WithHttpHealthCheck(".well-known/openid-configuration")
                .WithDetailsOnlyUrls("🪪 Identity provider authority");

            var idpAudience = builder.AddParameter("IdpAudience", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Identity Provider Audience",
                    Description = "The audience for the identity provider.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "portal",
                    Placeholder = "e.g. portal"
                });

            var idpAdditionalAudiences = builder.AddParameter("IdpAdditionalAudiences", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Identity Provider Additional Audiences",
                    Description = "Optional additional audiences accepted by the gateway.",
                    InputType = InputType.Text,
                    Required = false,
                    Value = "account",
                    Placeholder = "e.g. account,api"
                });

            var introspectionClientId = builder.AddParameter("IntrospectionClientId", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Introspection Client ID",
                    Description = "The client ID for token introspection.",
                    InputType = InputType.Text,
                    Required = false,
                    Value = "portal",
                    Placeholder = "e.g. userorg-gateway-introspection-client"
                });

            var introspectionClientSecret = builder.AddParameter("IntrospectionClientSecret", new RuntimeDefault(""), secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Introspection Client Secret",
                    Description = "The client secret for token introspection.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Placeholder = "e.g. supersecret"
                });

            var introspectionEndpoint = builder.AddParameter("IntrospectionEndpoint", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Introspection Endpoint",
                    Description = "The explicit introspection endpoint URL (optional).",
                    InputType = InputType.Text,
                    Required = false,
                    Value = "https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/protocol/openid-connect/token/introspect",
                    Placeholder = "e.g. https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV/protocol/openid-connect/token/introspect"
                });

            var enableIntrospection = builder.AddParameter("EnableIntrospection", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Enable Introspection",
                    Description = "Enable token introspection for the identity provider.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var tokenExchangeClientId = builder.AddParameter("TokenExchangeClientId", new RuntimeDefault(""), secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Token Exchange Client ID",
                    Description = "The client ID that exchanges the caller's token per HEAppE instance.",
                    InputType = InputType.Text,
                    Required = false,
                    Placeholder = "e.g. userorg-token-exchange"
                });

            var tokenExchangeClientSecret = builder.AddParameter("TokenExchangeClientSecret", new RuntimeDefault(""), secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Token Exchange Client Secret",
                    Description = "The client secret of the token exchange client.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Placeholder = "e.g. supersecret"
                });

            var externalIdentityProvider = new ExternalIdentityProviderParameters(
                idpAuthorityService,
                idpAudience,
                idpAdditionalAudiences,
                introspectionClientId,
                introspectionClientSecret,
                introspectionEndpoint,
                enableIntrospection,
                tokenExchangeClientId,
                tokenExchangeClientSecret);

            var dbPassword = builder.AddParameter("DatabasePassword", secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Database SA Password",
                    Description = "The system administrator password for the SQL Server database.",
                    InputType = InputType.SecretText,
                    Required = true,
                    Value = "P@ssw0rd",
                    Placeholder = "e.g. P@ssw0rd"
                });

            var cyclopsBaseUrl = builder.AddParameter("cyclopsBaseUrl", new RuntimeDefault("https://vm1.stage.cyclops.lexis.it4i.cz"), secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Cyclops Base URL",
                    Description = "The base URL for the Cyclops service.",
                    InputType = InputType.Text,
                    Required = true,
                    Placeholder = "e.g. https://cyclops.dev.lexis.tech"
                });

            var cyclopsBaseUrlService = builder.AddExternalService("cyclops", cyclopsBaseUrl)
                .WithDetailsOnlyUrls("💶 Cyclops");

            var cyclopsTimeUsageRelativeUrl = builder.AddParameter("cyclopsTimeUsageRelativeUrl", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Cyclops Time Usage Relative URL",
                    Description = "The relative URL of the Cyclops per-record usage endpoint.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "/udrAPI/api/v1.0/usage",
                    Placeholder = "e.g. /udrAPI/api/v1.0/usage"
                });

            var cyclopsTotalUsageRelativeUrl = builder.AddParameter("cyclopsTotalUsageRelativeUrl", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Cyclops Total Usage Relative URL",
                    Description = "The relative URL of the Cyclops aggregated usage endpoint.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "/udrAPI/api/v1.0/usage/aggregate",
                    Placeholder = "e.g. /udrAPI/api/v1.0/usage/aggregate"
                });

            var enableWaldurSyncIntegration = builder.AddParameter("EnableWaldurSyncIntegration", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Enable Waldur Sync Integration",
                    Description = "Enable synchronization with Waldur for project and user management.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var waldurBaseUrl = builder.AddParameter("WaldurBaseUrl", new RuntimeDefault("https://allocations.dev.my-eurohpc.eu"), secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Waldur Base URL",
                    Description = "The base URL for the Waldur service.",
                    InputType = InputType.Text,
                    Required = true,
                    Placeholder = "e.g. https://allocations.dev.my-eurohpc.eu"
                });

            var waldurBaseUrlService = builder.AddExternalService("waldur", waldurBaseUrl)
                .WithHttpHealthCheck("/api/", 401)
                .WithUrls(context =>
                {
                    foreach (var url in context.Urls)
                    {
                        url.DisplayText = "🌐 Waldur";
                    }

                    return Task.CompletedTask;
                });

            var waldurSyncIntegrationPeriod = builder.AddParameter("WaldurSyncIntegrationPeriod", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Waldur Sync Integration Period",
                    Description = "The synchronization period in minutes for Waldur integration.",
                    InputType = InputType.Number,
                    Required = true,
                    Value = "5",
                    Placeholder = "e.g. 60"
                });

            var roleManagementAccessToken = builder.AddParameter(
                    "RoleManagementAccessToken",
                    new GenerateParameterDefault { MinLength = 32, Special = false },
                    secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Role Management Access Token",
                    Description = "The access token for role management operations.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Placeholder = "e.g. supersecrettoken"
                });

            // Not a setting under `aspire run`, so it is not registered as one. The gateway's port is
            // allocated per run and this parameter is resolved before any endpoint exists, so no value
            // that could be entered here is right for longer than one run - the hardcoded :5800 default
            // was wrong from the moment it was written. Left unset, the OpenAPI document carries no
            // servers entry and ASP.NET Core fills one per request from the host header, so the API
            // explorer is always same-origin with the page that loaded it, whichever host and port that
            // is. A value here can only override that with a worse one. A deployment does have a fixed
            // public URL, and there it is a real setting.
            var scalarServerUrl = builder.ExecutionContext.IsPublishMode
                ? builder.AddParameter("ScalarServerUrl", secret: false)
                    .WithCustomInput(parameter => new()
                    {
                        Name = "Scalar Server URL",
                        Description = "The public server URL written into the OpenAPI document.",
                        InputType = InputType.Text,
                        Required = false,
                        Placeholder = "e.g. https://gateway.example.com"
                    })
                : null;

            var gatewayInternalAudience = builder.AddParameter("GatewayInternalAudience", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Gateway Internal Audience",
                    Description = "The JWT audience for service-to-service (gateway → API) tokens.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "gateway-internal",
                    Placeholder = "e.g. gateway-internal"
                });

            var featureReadOnlyMode = builder.AddParameter("FeatureReadOnlyMode", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Read-Only Mode",
                    Description = "When true, the API rejects all write operations.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            // The two Core API surfaces, switched independently so a run can serve v1 only, v2 only, or
            // both. Both default to true: v1 because the API itself treats an absent flag as enabled, v2
            // because the API defaults it off and every local run wants the refactored endpoints. Each is a
            // one-way switch in the API — turning it off at run time 404s the routes, turning it on needs a
            // restart, because the endpoints are only mapped when the flag was set at startup.
            var featureV1Api = builder.AddParameter("FeatureV1Api", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "v1 API enabled",
                    Description = "When true, the legacy (v1) Core endpoints are mapped and served. Turn off to run the v2 surface alone.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "true",
                    Placeholder = "e.g. false"
                });

            var featureV2Api = builder.AddParameter("FeatureV2Api", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "v2 API enabled",
                    Description = "When true, the modular (v2) Core endpoints and their persistence units and background workers are registered and served.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "true",
                    Placeholder = "e.g. false"
                });

            var featureTokenExchangeEnabled = builder.AddParameter("FeatureTokenExchangeEnabled", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "HEAppE token exchange enabled",
                    Description = "When true, the api exchanges the caller's token for one per HEAppE instance Keycloak client before every user call (ADR-0118); false forwards the caller's token.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var featureStrictJwtAudience = builder.AddParameter("FeatureStrictJwtAudience", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Strict JWT audience",
                    Description = "When true, the gateway rejects a user token carrying any audience other than Idp:Audience and Idp:AdditionalAudiences (ADR-0121).",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "true",
                    Placeholder = "e.g. false"
                });

            var autoMigrateOnStartup = builder.AddParameter("AutoMigrateOnStartup", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Auto-Migrate On Startup",
                    Description = "When true, the API and Gateway apply pending EF Core migrations on startup. Leave false when the DatabaseMigrations tool owns applying (local SQL container).",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var serilogBaseLevel = builder.AddParameter("SerilogBaseLevel", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Base Level",
                    Description = "Minimum log level when step-up is not active.",
                    InputType = InputType.Choice,
                    Required = true,
                    Value = "Warning",
                    Options = [new("Verbose", "Verbose"), new("Debug", "Debug"), new("Information", "Information"), new("Warning", "Warning"), new("Error", "Error"), new("Fatal", "Fatal")]
                });

            var serilogStepUpLevel = builder.AddParameter("SerilogStepUpLevel", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Step-Up Level",
                    Description = "Minimum log level while step-up logging is active.",
                    InputType = InputType.Choice,
                    Required = true,
                    Value = "Information",
                    Options = [new("Verbose", "Verbose"), new("Debug", "Debug"), new("Information", "Information"), new("Warning", "Warning"), new("Error", "Error"), new("Fatal", "Fatal")]
                });

            var serilogCaptureRequestBody = builder.AddParameter("SerilogCaptureRequestBody", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Capture Request Body",
                    Description = "Include the HTTP request body in log entries.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. false"
                });

            var serilogAlwaysLogRequestSummary = builder.AddParameter("SerilogAlwaysLogRequestSummary", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Always Log Request Summary",
                    Description = "Always emit a one-line request summary regardless of step-up state.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "true",
                    Placeholder = "e.g. false"
                });

            var serilogMode = builder.AddParameter("SerilogMode", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Mode",
                    Description = "Step-up logging mode applied to both the API and Gateway.",
                    InputType = InputType.Choice,
                    Required = true,
                    Value = "Auto",
                    Options = [new("Auto", "Auto — activates step-up on demand"), new("AlwaysOn", "AlwaysOn — always logs at step-up level"), new("Disabled", "Disabled — step-up never activates"), new("Diagnostic", "Diagnostic — logs Debug for SerilogStepUp:DiagnosticDurationMinutes (default 30) after start, then runs as Auto; it re-arms on every start, so set it back to Auto after the session")]
                });

            var serilogEnableConsoleLogging = builder.AddParameter("SerilogEnableConsoleLogging", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Serilog Console Logging",
                    Description = "Write logs to stdout in both the API and Gateway.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var defaultGatewayRoleManagementSeedFilePath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "roleManagmentRBAC.json"));

            // Run mode only: publishing the machine-specific path into the manifest would break portability.
            var gatewayRoleManagementSeedFilePath = (builder.ExecutionContext.IsRunMode
                    ? builder.AddParameter("GatewayRoleManagementSeedFilePath", new RuntimeDefault(defaultGatewayRoleManagementSeedFilePath), secret: false)
                    : builder.AddParameter("GatewayRoleManagementSeedFilePath", secret: false))
                .WithCustomInput(parameter => new()
                {
                    Name = "Role Management Seed File Path",
                    Description = "Absolute or relative path to the RBAC seed JSON file loaded by the Gateway.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = defaultGatewayRoleManagementSeedFilePath,
                    Placeholder = "e.g. <repo-root>/roleManagmentRBAC.json"
                });

            var enableInactiveUserSync = builder.AddParameter("EnableInactiveUserSync", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Enable Inactive User Sync",
                    Description = "Enable synchronization of inactive users with the external identity provider.",
                    InputType = InputType.Boolean,
                    Required = true,
                    Value = "false",
                    Placeholder = "e.g. true"
                });

            var inactiveUserSyncBaseUrl = builder.AddParameter("InactiveUserSyncBaseUrl", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Inactive User Sync Base URL",
                    Description = "The base URL for the inactive user synchronization endpoint.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV",
                    Placeholder = "e.g. https://aai.dev.lexis.tech/auth/realms/LEXIS_AAI_v2_DEV"
                });

            var inactiveUserSyncClientId = builder.AddParameter("InactiveUserSyncClientId", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Inactive User Sync Client ID",
                    Description = "The client ID for the inactive user synchronization endpoint.",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "lexis-auth-cli",
                    Placeholder = "e.g. lexis-auth-cli"
                });

            var inactiveUserSyncClientSecret = builder.AddParameter("InactiveUserSyncClientSecret", new RuntimeDefault(""), secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Inactive User Sync Client Secret",
                    Description = "The client secret for the inactive user synchronization endpoint. Only read when inactive user sync is enabled.",
                    InputType = InputType.SecretText,
                    // EnableInactiveUserSync is off by default: a secret this run will never read must
                    // not be what the dialog demands before the gateway is allowed to start.
                    Required = false,
                });

            var inactiveUserSyncInterval = builder.AddParameter("InactiveUserSyncInterval", secret: false)
                .WithCustomInput(parameter => new()
                {
                    Name = "Inactive User Sync Interval",
                    Description = "The interval between inactive user syncs, as a TimeSpan (d.hh:mm:ss).",
                    InputType = InputType.Text,
                    Required = true,
                    Value = "7.00:00:00",
                    Placeholder = "e.g. 7.00:00:00"
                });

            var emailSenderApiKey = builder.AddParameter(
                    "EmailSenderApiKey",
                    new GenerateParameterDefault { MinLength = 32, Special = false },
                    secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Email Sender API Key",
                    Description = "The API key the email sender requires in the X-Api-Key header of every request.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Placeholder = "e.g. supersecretkey"
                });

            var emailSenderBrowserToken = builder.AddParameter(
                    "EmailSenderBrowserToken",
                    new GenerateParameterDefault { MinLength = 32, Special = false },
                    secret: true)
                .WithCustomInput(parameter => new()
                {
                    Name = "Email Sender Browser Token",
                    Description = "The token that opens the email sender template UI in a browser.",
                    InputType = InputType.SecretText,
                    Required = false,
                    Placeholder = "e.g. supersecrettoken"
                });

            return new Parameters(
                userorgDev,
                remoteDatabase,
                dbPassword,
                cyclopsBaseUrlService,
                cyclopsTimeUsageRelativeUrl,
                cyclopsTotalUsageRelativeUrl,
                enableWaldurSyncIntegration,
                waldurBaseUrlService,
                waldurSyncIntegrationPeriod,
                roleManagementAccessToken,
                externalIdentityProvider,
                scalarServerUrl,
                gatewayInternalAudience,
                featureReadOnlyMode,
                featureV1Api,
                featureV2Api,
                featureTokenExchangeEnabled,
                featureStrictJwtAudience,
                autoMigrateOnStartup,
                serilogBaseLevel,
                serilogStepUpLevel,
                serilogCaptureRequestBody,
                serilogAlwaysLogRequestSummary,
                serilogMode,
                serilogEnableConsoleLogging,
                gatewayRoleManagementSeedFilePath,
                enableInactiveUserSync,
                inactiveUserSyncBaseUrl,
                inactiveUserSyncClientId,
                inactiveUserSyncClientSecret,
                inactiveUserSyncInterval,
                emailSenderApiKey,
                emailSenderBrowserToken
            );
        }
    }
}