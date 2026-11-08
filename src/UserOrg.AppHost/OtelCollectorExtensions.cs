namespace UserOrg.AppHost;

internal static class OtelCollectorExtensions
{
    /// <summary>The path the contrib image's entrypoint reads its configuration from.</summary>
    private const string ConfigDirectory = "/etc/otelcol-contrib";

    private const string ConfigFileName = "config.yaml";

    /// <summary>The header Aspire puts the dashboard's OTLP key in, when the dashboard asks for one.</summary>
    private const string OtlpHeadersVariable = "OTEL_EXPORTER_OTLP_HEADERS";

    private const string ApiKeyHeaderName = "x-otlp-api-key";

    /// <summary>The variable the collector's configuration reads that key back out of.</summary>
    private const string ApiKeyVariable = "ASPIRE_OTLP_API_KEY";

    private static string ConfigContents { get; } = VaultSetup.ReadEmbeddedText("otel-collector-config.yaml");

    /// <summary>
    /// The OpenTelemetry collector every producer in this app host exports to, forwarding to the Aspire
    /// dashboard.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than taken from CommunityToolkit.Aspire.Hosting.OpenTelemetryCollector: that
    /// package exists only as a beta while every CommunityToolkit package this repository takes is pinned
    /// to a stable 13.5.0, and the container below is thirty lines.
    /// </remarks>
    public static IResourceBuilder<ContainerResource> AddOtelCollector(this IDistributedApplicationBuilder builder)
    {
        return builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib", "0.160.0")
            .WithIconName("DataTrending")
            // Session, not Persistent: the collector holds nothing that has to outlive a run, and a
            // persistent one would keep exporting to the dashboard endpoint of a run that has ended.
            .WithLifetime(ContainerLifetime.Session)
            .WithContainerFiles(
                ConfigDirectory,
                [
                    new ContainerFile
                    {
                        Name = ConfigFileName,
                        Contents = ConfigContents.AsContainerFileContents(builder)
                    }
                ])
            .WithEndpoint(targetPort: 4317, scheme: "tcp", name: "grpc")
            .WithHttpEndpoint(targetPort: 4318, name: "http")
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "📡 OTLP over HTTP";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithUrlForEndpoint("grpc", static url =>
            {
                url.DisplayText = "📡 OTLP over gRPC";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            // Points this container at the dashboard, from inside a container rather than from the host.
            .WithOtlpExporter()
            .WithEnvironment(static context =>
            {
                // Split here rather than in the collector's configuration: its ${env:} substitution
                // resolves a whole value, and what Aspire injects is a header list - `x-otlp-api-key=…` -
                // not the key on its own. Absent whenever the dashboard runs without OTLP authentication,
                // which is the default locally, and the configuration defaults the header to empty.
                if (context.EnvironmentVariables.TryGetValue(OtlpHeadersVariable, out var headers)
                    && headers is string list)
                {
                    var key = list
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(header => header.Split('=', 2))
                        .FirstOrDefault(pair =>
                            pair.Length == 2
                            && pair[0].Equals(ApiKeyHeaderName, StringComparison.OrdinalIgnoreCase));

                    if (key is not null)
                    {
                        context.EnvironmentVariables[ApiKeyVariable] = key[1];
                    }
                }
            });
    }

    /// <summary>
    /// Sends a producer's telemetry to <paramref name="collector"/> instead of straight to the dashboard.
    /// </summary>
    /// <remarks>
    /// Overwrites what Aspire already injected. The producers run as host processes under
    /// <c>aspire run</c>, so the endpoint they need is the collector's host endpoint, not the address the
    /// collector itself was handed. http/protobuf rather than the gRPC default: the collector receives
    /// both, and an HTTP endpoint is the one a developer can also reach with curl while debugging why a
    /// span did not arrive.
    ///
    /// No <c>WaitFor</c>: the endpoint resolves from the allocation without the container running, the
    /// exporter retries, and the collector declares no health check - so a wait would release on "created"
    /// rather than "receiving" and buy nothing, while turning a registry hiccup on the collector image
    /// into two projects that never start at all. A missing collector costs spans, not the run.
    /// </remarks>
    public static IResourceBuilder<ProjectResource> WithOtelCollectorEnvironment(
        this IResourceBuilder<ProjectResource> producer,
        IResourceBuilder<ContainerResource> collector)
    {
        return producer
            .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", collector.GetEndpoint("http"))
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
    }
}