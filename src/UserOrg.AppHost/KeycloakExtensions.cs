using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UserOrg.AppHost;

internal static class KeycloakExtensions
{
    /// <summary>
    /// A login the imported realm carries, and the forge role the database seed gives the user behind it.
    /// </summary>
    /// <param name="Username">The realm username; also what the gettoken command posts.</param>
    /// <param name="Role">The UserOrg role the database seed gives the user.</param>
    internal sealed record SeededLogin(string Username, string Role)
    {
        /// <summary>What the gettoken dropdown shows.</summary>
        public string Label => $"{Username} — {Role}";
    }

    /// <summary>
    /// The realm's password is the same for every seeded login; the realm is imported fresh on every run and
    /// reaches nothing outside this machine.
    /// </summary>
    internal const string SeededPassword = "test";

    /// <summary>
    /// Every login the realm import creates, in the order the gettoken dropdown offers them. The user ids
    /// behind the role-coverage logins are the <c>IdentityProviderUserId</c> values
    /// <c>Tests/E2E/E2E.Fixtures/seed-data.sql</c> inserts, so a token minted here resolves to that user's
    /// permissions. The two files move together: a new login needs a matching seed row to be worth anything.
    /// </summary>
    internal static readonly IReadOnlyList<SeededLogin> SeededLogins =
    [
        new("test", "Admin, project owner of TESTPRJ"),
        new("test2", "User, project manager of TESTPRJ"),
        new("test3", "User, project member of TESTPRJ"),
        new("provideradmin", "ProviderAdmin"),
        new("datapublisher", "DataPublisher on TESTPRJ"),
        new("jobscriptoperator", "JobscriptOperator on TESTPRJ"),
        new("jobscriptwriter", "JobscriptWriter on TESTPRJ"),
        new("jobscriptaccessmember", "JobscriptAccessMember on TESTPRJ")
    ];

    /// <summary>The dropdown entry for the realm's own administrator, which has no UserOrg identity.</summary>
    private const string AdminLogin = "admin";

    private const string AdminRole = "Keycloak realm administrator, no UserOrg identity";

    /// <summary>The realm's confidential client that gettoken and the gateway use.</summary>
    internal const string TestClientId = "testClient";

    /// <summary>The secret of <see cref="TestClientId"/> the realm import sets.</summary>
    internal const string TestClientSecret = "testClient";

    /// <summary>The realm's public PKCE client the Scalar login uses, so no client secret reaches the browser.</summary>
    internal const string ScalarClientId = "scalar";

    /// <summary>The Keycloak container variable the realm import resolves to the gateway's origin.</summary>
    internal const string GatewayOriginVariable = "USERORG_GATEWAY_ORIGIN";

    /// <summary>The Keycloak container variable the realm import resolves to the API's origin.</summary>
    internal const string ApiOriginVariable = "USERORG_API_ORIGIN";

    /// <summary>
    /// Appended to an origin variable for the origin the dashboard links to, which uses the endpoint's
    /// <c>*.dev.localhost</c> target host instead of the <c>localhost</c> the project binds.
    /// </summary>
    internal const string DashboardOriginSuffix = "_DASHBOARD";

    public static (IResourceBuilder<ContainerResource>, IResourceBuilder<ParameterResource>, IResourceBuilder<ParameterResource>) AddKeycloakService(this IDistributedApplicationBuilder builder)
    {
        var keycloakUsername = builder.AddParameter("KeycloakUsername")
            .WithCustomInput(parameter => new()
            {
                Name = "Keycloak Username",
                Description = "The username for the Keycloak admin user.",
                InputType = InputType.Text,
                Required = true,
                Value = "admin",
                Placeholder = "e.g. admin"
            });
        var keycloakPassword = builder.AddParameter("KeycloakPassword", secret: true)
            .WithCustomInput(parameter => new()
            {
                Name = "Keycloak Password",
                Description = "The password for the Keycloak admin user.",
                InputType = InputType.SecretText,
                Required = true,
                Value = "admin",
                Placeholder = "e.g. admin"
            });

        var keycloak = builder.AddKeycloak("Idp", adminUsername: keycloakUsername, adminPassword: keycloakPassword)
            .WithOtlpExporter()
            .WithRealmImport(Path.Combine(builder.AppHostDirectory, "externalServices/keycloakRealm/master-realm.json"))
            // In the model whatever the identity-provider choice turns out to be, because that choice is only
            // known once the dashboard dialog is answered, which happens after Build() (T-071 E1).
            // IdentityProviderSelection starts this container when the answer is LocalKeycloak.
            .WithExplicitStart()
            .ExcludeFromManifest()
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🔐 Keycloak";
                url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
            })
            .WithUrlForEndpoint("management", static url =>
            {
                url.DisplayText = "⚙️ Keycloak management";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            });

        return (keycloak, keycloakUsername, keycloakPassword);
    }

    /// <summary>The issuer of the imported realm.</summary>
    public static ReferenceExpression RealmAuthority(this IResourceBuilder<ContainerResource> keycloak) =>
        ReferenceExpression.Create($"{keycloak.Resource.GetEndpoint("http")}/realms/master");

    /// <summary>The discovery document of the imported realm.</summary>
    public static ReferenceExpression RealmMetadataAddress(this IResourceBuilder<ContainerResource> keycloak) =>
        ReferenceExpression.Create($"{keycloak.Resource.GetEndpoint("http")}/realms/master/.well-known/openid-configuration");

    /// <summary>The authorization endpoint of the imported realm.</summary>
    public static ReferenceExpression RealmAuthorizationUrl(this IResourceBuilder<ContainerResource> keycloak) =>
        ReferenceExpression.Create($"{keycloak.Resource.GetEndpoint("http")}/realms/master/protocol/openid-connect/auth");

    /// <summary>The token endpoint of the imported realm.</summary>
    public static ReferenceExpression RealmTokenUrl(this IResourceBuilder<ContainerResource> keycloak) =>
        ReferenceExpression.Create($"{keycloak.Resource.GetEndpoint("http")}/realms/master/protocol/openid-connect/token");

    /// <summary>
    /// Hands Keycloak the project's resolved https origin under <paramref name="variable"/>, and the origin the
    /// dashboard links to under <paramref name="variable"/> + <see cref="DashboardOriginSuffix"/>, so the realm
    /// import accepts both as a redirect URI and web origin of <see cref="ScalarClientId"/>.
    /// Both the gateway and the API serve a Scalar page that logs in through that client.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithClientOrigin(
        this IResourceBuilder<ContainerResource> keycloak,
        string variable,
        IResourceBuilder<ProjectResource> project)
    {
        var dashboardHost = DashboardHostOf(project.Resource.Name, keycloak.ApplicationBuilder.Configuration["ASPNETCORE_URLS"]);

        return keycloak.WithEnvironment(async context =>
        {
            if (await project.GetEndpoint("https").GetValueAsync(context.CancellationToken) is { } origin)
            {
                context.EnvironmentVariables[variable] = origin;
                context.EnvironmentVariables[variable + DashboardOriginSuffix] = dashboardHost is null
                    ? origin
                    : new UriBuilder(origin) { Host = dashboardHost }.Uri.GetLeftPart(UriPartial.Authority);
            }
        });
    }

    /// <summary>
    /// The host the dashboard links a resource to when the dashboard itself runs on a <c>*.dev.localhost</c> URL:
    /// <c>&lt;resource&gt;-&lt;dashboard subdomain without an "apphost" suffix&gt;.dev.localhost</c>, the rule of
    /// Aspire's ApplicationOrchestrator. <see langword="null"/> when the dashboard runs on any other host.
    /// </summary>
    internal static string? DashboardHostOf(string resourceName, string? dashboardUrls)
    {
        const string DevLocalhost = ".dev.localhost";
        const string AppHostSuffix = "apphost";

        if (!Uri.TryCreate(dashboardUrls?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), UriKind.Absolute, out var dashboard)
            || !EndpointHostHelpers.IsDevLocalhostTld(dashboard))
        {
            return null;
        }

        var subdomain = dashboard.Host[..dashboard.Host.IndexOf(DevLocalhost, StringComparison.OrdinalIgnoreCase)];
        var suffix = "-_.".Select(separator => $"{separator}{AppHostSuffix}")
            .FirstOrDefault(candidate => subdomain.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));

        return $"{resourceName}-{(suffix is null ? subdomain : subdomain[..^suffix.Length])}{DevLocalhost}";
    }

    /// <summary>
    /// The dashboard command that lists the logins the imported realm carries and their passwords, for signing
    /// in by hand (Scalar's OIDC login, the Keycloak account console). Enabled once Keycloak is healthy, which it
    /// only becomes when the run uses the local Keycloak.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithLoginsCommand(
        this IResourceBuilder<ContainerResource> keycloak,
        IResourceBuilder<ParameterResource> keycloakUsername,
        IResourceBuilder<ParameterResource> keycloakPassword) =>
        keycloak.WithCommand("logins", "Show Logins", commandOptions: new()
        {
            IconName = "People",
            IsHighlighted = true,
            UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                ? ResourceCommandState.Enabled
                : ResourceCommandState.Disabled
        },
        executeCommand: async context =>
        {
            var adminUsername = await keycloakUsername.Resource.GetValueAsync(context.CancellationToken);
            var adminPassword = await keycloakPassword.Resource.GetValueAsync(context.CancellationToken);

            return CommandResults.Success(
                "Realm logins listed.",
                LoginsTable(adminUsername, adminPassword),
                CommandResultFormat.Markdown,
                displayImmediately: true);
        });

    internal static string LoginsTable(string? adminUsername, string? adminPassword) =>
        string.Join('\n',
        [
            "| Username | Password | Role |",
            "|---|---|---|",
            .. SeededLogins.Select(login => $"| `{login.Username}` | `{SeededPassword}` | {login.Role} |"),
            $"| `{adminUsername}` | `{adminPassword}` | {AdminRole} |"
        ]);

    /// <summary>
    /// The dashboard command that mints an access token for one seeded login through the realm's direct
    /// access grant. Registered whatever the identity-provider choice turns out to be; on the external
    /// authority there is no local realm to ask, and the command says so instead of posting into the void.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithGetTokenCommand(
        this IResourceBuilder<ProjectResource> gateway,
        IResourceBuilder<ContainerResource> keycloak,
        IResourceBuilder<ParameterResource> keycloakUsername,
        IResourceBuilder<ParameterResource> keycloakPassword,
        IdentityProviderSelection identityProvider) =>
        gateway.WithCommand("gettoken", "Get Access Token", commandOptions: new()
        {
            IconName = "Key",
            IsHighlighted = true,
            Arguments =
            [
                new InteractionInput
                {
                    Name = "user",
                    Label = "User",
                    InputType = InputType.Choice,
                    Required = true,
                    Value = SeededLogins[0].Username,
                    Options =
                    [
                        .. SeededLogins.Select(login => new KeyValuePair<string, string>(login.Username, login.Label)),
                        new(AdminLogin, $"{AdminLogin} — {AdminRole}")
                    ]
                }
            ]
        },
        executeCommand: async context =>
        {
            if (!await identityProvider.UseLocalKeycloakAsync(context.CancellationToken))
            {
                return CommandResults.Failure(
                    "This run authenticates against the external identity provider, so there is no local realm to issue a token. Restart the app host and choose the local Keycloak container.");
            }

            var user = context.Arguments["user"].Value ?? string.Empty;

            string username;
            string password;

            if (user == AdminLogin)
            {
                username = await keycloakUsername.Resource.GetValueAsync(context.CancellationToken);
                password = await keycloakPassword.Resource.GetValueAsync(context.CancellationToken);
            }
            else if (SeededLogins.Any(login => login.Username == user))
            {
                username = user;
                password = SeededPassword;
            }
            else
            {
                return CommandResults.Failure($"Unknown gettoken user '{user}'.");
            }

            var endpoint = $"{keycloak.GetEndpoint("http").Url}/realms/master/protocol/openid-connect/token";
            using var httpClient = new HttpClient();

            var result = await httpClient.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = TestClientId,
                ["client_secret"] = TestClientSecret,
                ["grant_type"] = "password",
                ["scope"] = "openid profile",
                ["username"] = username,
                ["password"] = password
            }));

            if (!result.IsSuccessStatusCode)
            {
                return CommandResults.Failure(result.StatusCode.ToString());
            }

            var responseContent = await result.Content.ReadAsStringAsync(context.CancellationToken);

            return CommandResults.Success($"Token retrieved for '{user}'.", responseContent, CommandResultFormat.Json, displayImmediately: true);
        });
}