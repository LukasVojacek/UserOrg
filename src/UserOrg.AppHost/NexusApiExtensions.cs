namespace UserOrg.AppHost;

/// <summary>
/// Gives the Core API the <c>nexusapi</c> service every HEAppE call goes through (#349). Under <c>aspire run</c> all
/// both modes are in the model and <see cref="NexusApiSelection"/> picks one after <c>Build()</c>, like the database
/// source and the identity provider: a container beside the host, its registry seeded with the HEAppE mock, or an
/// external NexusApi at the <c>NexusApiBaseUrl</c> parameter. Publish mode always takes the external one.
/// </summary>
internal static class NexusApiExtensions
{
    private const string DefaultImage = "opencode.it4i.eu:5050/lexis-platform/backend/nexusapi/api";

    private const string DefaultImageTag = "0.0.0-rc.357";

    private const string ImportDirectory = "/config";

    private const string ImportFileName = "nexusapi-import.json";

    /// <summary>
    /// Points the API at NexusApi and returns the explicit-start container chain <see cref="NexusApiSelection"/>
    /// starts when the container is chosen (empty in publish mode). There is deliberately no <c>WaitFor</c>: the
    /// NexusApi image comes from a private registry, and the rest of the host has to start when it cannot be
    /// pulled (Q1). The API's own HEAppE calls answer 503 until it is reachable.
    /// </summary>
    internal static IReadOnlyList<IResource> WithNexusApi(
        this IResourceBuilder<ProjectResource> api,
        IDistributedApplicationBuilder builder,
        IResourceBuilder<WireMockServerResource>? heappeMock,
        NexusApiSelection selection,
        IResourceBuilder<ParameterResource> tokenExchangeEnabled)
    {
        var client = builder.AddParameter("NexusApiServiceAccountClient", new RuntimeDefault(NexusApiModes.DefaultServiceAccountClient), secret: false);

        if (!builder.ExecutionContext.IsRunMode)
        {
            api.WithEnvironment("TokenExchange__Nexus__BaseUrl", builder.AddNexusExternal(builder.AddNexusBaseUrl()))
                .WithEnvironment("TokenExchange__Nexus__ClientId", builder.AddExternalServiceAccountClient())
                .WithEnvironment("TokenExchange__Nexus__ClientSecret", builder.AddExternalServiceAccountSecret());
            return [];
        }

        // Generated here and written into the import file, so the container and the API always agree.
        var containerSecret = builder.AddContainerSecret("NexusApiContainerServiceAccountSecret", selection);
        var (container, chain) = builder.AddNexusContainer(client, containerSecret, heappeMock, selection);

        api.WithEnvironment(async context =>
        {
            var choice = await selection.ChoiceAsync(context.CancellationToken);
            var serviceAccount = NexusApiModes.ParseTokenExchangeEnabled(
                await tokenExchangeEnabled.Resource.GetValueAsync(context.CancellationToken));

            if (choice.Mode == NexusApiMode.Container)
            {
                context.EnvironmentVariables["TokenExchange__Nexus__BaseUrl"] = container.GetEndpoint("http");
                if (serviceAccount)
                {
                    context.EnvironmentVariables["TokenExchange__Nexus__ClientId"] = client.Resource;
                    context.EnvironmentVariables["TokenExchange__Nexus__ClientSecret"] = containerSecret.Resource;
                }

                return;
            }

            context.EnvironmentVariables["TokenExchange__Nexus__BaseUrl"] = choice.BaseUrl!.ToString().TrimEnd('/');
            if (serviceAccount)
            {
                context.EnvironmentVariables["TokenExchange__Nexus__ClientId"] = choice.ClientId!;
                context.EnvironmentVariables["TokenExchange__Nexus__ClientSecret"] = choice.ServiceAccountSecret!;
            }
        });

        return chain;
    }

    private static IResourceBuilder<ParameterResource> AddNexusBaseUrl(this IDistributedApplicationBuilder builder) =>
        builder.AddParameter("NexusApiBaseUrl", secret: false)
            .WithCustomInput(parameter => new()
            {
                Name = "NexusApi Base URL",
                Description = "The base URL of the NexusApi that proxies HEAppE, without a trailing slash. Only used by the external NexusApi.",
                InputType = InputType.Text,
                Required = true,
                Placeholder = "e.g. https://nexusapi.dev.lexis.tech",
            });

    // Publish mode only: run mode reads all three from user secrets and asks for missing ones in NexusApiPrompt.AskExternalAsync, only for the external NexusApi.
    // The id of an account that already exists on the external NexusApi.
    private static IResourceBuilder<ParameterResource> AddExternalServiceAccountClient(this IDistributedApplicationBuilder builder) =>
        builder.AddParameter("NexusApiServiceAccountClient", new RuntimeDefault(""), secret: false)
            .WithCustomInput(parameter => new()
            {
                Name = "NexusApi Service Account Client",
                Description = "The id of the service account on the external NexusApi, only used with FeatureTokenExchangeEnabled.",
                InputType = InputType.Text,
                Required = false,
            });

    // Publish mode only: run mode reads it from user secrets and asks for it in NexusApiPrompt.AskExternalAsync when missing, only for the external NexusApi.
    // The secret of an account that already exists on the external NexusApi, which only the person running the host knows.
    private static IResourceBuilder<ParameterResource> AddExternalServiceAccountSecret(this IDistributedApplicationBuilder builder) =>
        builder.AddParameter("NexusApiServiceAccountSecret", new RuntimeDefault(""), secret: true)
            .WithCustomInput(parameter => new()
            {
                Name = "NexusApi Service Account Secret",
                Description = "The secret of the NexusApiServiceAccountClient account on the external NexusApi, only used with FeatureTokenExchangeEnabled.",
                InputType = InputType.SecretText,
                Required = false,
            });

    // Not persisted by Aspire, which would write it on every run: NexusApiSelection saves it to user secrets only when
    // the local container is chosen. Until then the configured value, or one generated for this run.
    private static IResourceBuilder<ParameterResource> AddContainerSecret(this IDistributedApplicationBuilder builder, string name, NexusApiSelection selection)
    {
        var parameter = builder.AddParameter(name, new RuntimeDefault(new GenerateParameterDefault { MinLength = 32, Special = false }.GetDefaultValue()), secret: true);
        selection.SaveWhenContainerChosen(parameter.Resource);
        return parameter;
    }

    private static IResourceBuilder<ExternalServiceResource> AddNexusExternal(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> baseUrl) =>
        // No health check: NexusApi maps /health only in Development.
        builder.AddExternalService("nexusapi", baseUrl)
            .WithDetailsOnlyUrls("🔗 NexusApi");

    // Every resource of the chain is explicit-start; NexusApiSelection starts them in this order when the container is chosen.
    private static (IResourceBuilder<ContainerResource> Server, IReadOnlyList<IResource> Chain) AddNexusContainer(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> serviceAccountClient,
        IResourceBuilder<ParameterResource> serviceAccountSecret,
        IResourceBuilder<WireMockServerResource>? heappeMock,
        NexusApiSelection selection)
    {
        var image = builder.BuildTimeValue(builder.AddParameter("NexusApiImage", new RuntimeDefault(DefaultImage), secret: false), DefaultImage);
        var tag = builder.BuildTimeValue(builder.AddParameter("NexusApiImageTag", new RuntimeDefault(DefaultImageTag), secret: false), DefaultImageTag);

        var postgresPassword = builder.AddContainerSecret("NexusApiContainerPosgresPassword", selection);
        var postgres = builder.AddPostgres("nexusapi-postgres", password: postgresPassword)
            .WithImageTag("17")
            .WithExplicitStart();
        var config = postgres.AddDatabase("config");

        var migrate = builder.AddNexusCommand("nexusapi-migrate", image, tag, config)
            .WithArgs("migrate");

        var import = builder.AddNexusCommand("nexusapi-import", image, tag, config)
            .WithArgs("import", $"{ImportDirectory}/{ImportFileName}")
            .WithContainerFiles(ImportDirectory, async (_, ct) =>
            [
                new ContainerFile
                {
                    Name = ImportFileName,
                    Contents = (await ImportDocumentAsync(serviceAccountClient, serviceAccountSecret, heappeMock, ct)).AsContainerFileContents(builder),
                },
            ])
            .WaitForCompletion(migrate);

        var server = builder.AddContainer("nexusapi", image)
            .WithImageTag(tag)
            .WithReference(config)
            .WithHttpEndpoint(targetPort: 8080, name: "http")
            .WithEnvironment("HTTP_PORTS", "8080")
            .WithEnvironment("RUN_MIGRATIONS", "false")
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🔗 NexusApi";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithExplicitStart()
            .WaitForCompletion(import);

        return (server, [postgres.Resource, migrate.Resource, import.Resource, server.Resource]);
    }

    // An image reference is a plain string in the model, so it cannot wait for the dashboard's parameter dialog:
    // the parameter is declared (it shows up with its default) and its configured value, or the default, is read
    // now. --Parameters:NexusApiImageTag=local is how a locally published image is used.
    private static string BuildTimeValue(this IDistributedApplicationBuilder builder, IResourceBuilder<ParameterResource> parameter, string fallback) =>
        builder.Configuration[$"Parameters:{parameter.Resource.Name}"] ?? fallback;

    // One-shot container of the NexusApi image: it runs a single command against the registry database and exits.
    private static IResourceBuilder<ContainerResource> AddNexusCommand(
        this IDistributedApplicationBuilder builder,
        string name,
        string image,
        string tag,
        IResourceBuilder<PostgresDatabaseResource> config) =>
        builder.AddContainer(name, image)
            .WithImageTag(tag)
            .WithReference(config)
            .WithExplicitStart()
            .WaitFor(config);

    private static async Task<string> ImportDocumentAsync(
        IResourceBuilder<ParameterResource> serviceAccountClient,
        IResourceBuilder<ParameterResource> serviceAccountSecret,
        IResourceBuilder<WireMockServerResource>? heappeMock,
        CancellationToken cancellationToken) =>
        NexusApiModes.ImportDocument(
            await RequiredValueAsync(serviceAccountClient, cancellationToken),
            await RequiredValueAsync(serviceAccountSecret, cancellationToken),
            UpstreamUrl(heappeMock));

    private static async Task<string> RequiredValueAsync(IResourceBuilder<ParameterResource> parameter, CancellationToken cancellationToken) =>
        await parameter.Resource.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Parameter '{parameter.Resource.Name}' has no value.");

    // The mock as the NexusApi container reaches it: by resource name on the container network, on the
    // port the WireMock image listens on.
    private static string? UpstreamUrl(IResourceBuilder<WireMockServerResource>? heappeMock) =>
        heappeMock is null ? null : $"http://{heappeMock.Resource.Name}:{heappeMock.GetEndpoint("http").TargetPort}";
}