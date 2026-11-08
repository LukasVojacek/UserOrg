using System.Text.RegularExpressions;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Pins the OpenTelemetry collector's composition. Every fact here is one that fails silently at run
/// time: a producer that names no collector endpoint exports to the dashboard instead and nothing says
/// so, a configuration file delivered by a bind mount works on the machine that wrote it and nowhere
/// else, and a floating image tag changes the collector under a run that changed nothing.
/// </summary>
public sealed class OtelCollectorCompositionTests
{
    private const string ResourceName = "otel-collector";

    private static InvocationExpressionSyntax Collector =>
        Root("OtelCollectorExtensions.cs").Call("AddContainer", ResourceName);

    [Fact]
    public void Should_PinTheCollectorImageTag()
    {
        var arguments = Collector.ArgumentList.Arguments;

        Assert.True(
            arguments.Count == 3,
            $"the {ResourceName} container names no image tag, so every run takes whatever `latest` resolves to that day");

        var tag = AppHostSyntax.StringValue(arguments[2].Expression);

        Assert.True(
            tag is not null && Regex.IsMatch(tag, @"^\d+\.\d+\.\d+$"),
            $"the {ResourceName} image tag is `{tag}`; expected an exact version");
    }

    [Fact]
    public void Should_DeliverTheCollectorConfigWithContainerFiles()
    {
        var deliveries = Collector.AppliedCalls("WithContainerFiles");

        Assert.True(
            deliveries.Count == 1,
            $"expected exactly one `WithContainerFiles` on the {ResourceName} container, found {deliveries.Count}; the collector's configuration is still delivered by a bind mount of the repository tree");

        Assert.Contains("ConfigFileName", deliveries[0].ArgumentList.Code(), StringComparison.Ordinal);
    }

    [Fact]
    public void Should_NotBindMountAnythingIntoTheCollector()
    {
        var mounts = Root("OtelCollectorExtensions.cs").Calls("WithBindMount");

        Assert.True(
            mounts.Count == 0,
            "OtelCollectorExtensions.cs bind-mounts a host path into the collector: "
                + string.Join("; ", mounts.Select(mount => mount.Describe())));
    }

    [Fact]
    public void Should_CarryTheCollectorConfigAsAnEmbeddedResource()
    {
        var project = File
            .ReadAllText(Path.Combine(AppHostSource.Directory, "UserOrg.AppHost.csproj"))
            .Replace('\\', '/');

        Assert.True(
            Regex.IsMatch(project, @"<EmbeddedResource[^>]*Include=""[^""]*otel-collector-config\.yaml"""),
            "UserOrg.AppHost.csproj does not embed the collector configuration; a file read from the repository tree at run time ties the app host to the directory it was built in");
    }

    [Fact]
    public void Should_NotPinAHostPortForTheCollector()
    {
        var endpoints = Collector.AppliedCalls("WithEndpoint")
            .Concat(Collector.AppliedCalls("WithHttpEndpoint"))
            .Where(call => call.ArgumentList.Arguments
                .Any(argument => argument.NameColon?.Name.Identifier.ValueText == "port"))
            .ToList();

        Assert.True(
            endpoints.Count == 0,
            "the collector pins a host port, which two app hosts on one machine cannot both have: "
                + string.Join("; ", endpoints.Select(endpoint => endpoint.Describe())));
    }

    [Fact]
    public void Should_ReachTheDashboardThroughTheEndpointAspireInjects()
    {
        var collector = Root("OtelCollectorExtensions.cs");

        Assert.True(
            collector.Calls("WithOtlpExporter").Count == 1,
            "the collector no longer calls WithOtlpExporter, so nothing tells it where the dashboard is and the endpoint its configuration reads is unset");
    }

    [Theory]
    [InlineData("userorg")]
    [InlineData("gateway")]
    public void Should_SendEveryProducerToTheCollector(string resourceName)
    {
        var producer = Root("Program.cs").Call("AddProject", resourceName);

        Assert.True(
            producer.AppliedCalls("WithOtelCollectorEnvironment").Count == 1,
            $"`{resourceName}` does not carry WithOtelCollectorEnvironment, so it exports straight to the dashboard and the collector sees none of its telemetry");
    }

    /// <summary>
    /// The producers run as host processes, so the address they need is the collector's host endpoint -
    /// the one Aspire allocates per run - and not the address the collector itself was handed, which
    /// points at the dashboard.
    /// </summary>
    [Fact]
    public void Should_PointProducersAtTheCollectorsOwnEndpoint()
    {
        var wiring = Root("OtelCollectorExtensions.cs")
            .Call("WithEnvironment", "OTEL_EXPORTER_OTLP_ENDPOINT")
            .ArgumentList.Code();

        Assert.Contains("collector.GetEndpoint", wiring, StringComparison.Ordinal);

        var protocol = Root("OtelCollectorExtensions.cs")
            .Call("WithEnvironment", "OTEL_EXPORTER_OTLP_PROTOCOL")
            .ArgumentList.Code();

        Assert.Contains("http/protobuf", protocol, StringComparison.Ordinal);
    }

    /// <summary>
    /// A collector that cannot be pulled must cost spans, not the run. The wait would also be decorative:
    /// the collector declares no health check, so it would release on "container created" rather than on
    /// "OTLP receiver listening".
    /// </summary>
    [Fact]
    public void Should_NotHoldTheProducersBehindTheCollector()
    {
        var wiring = Root("OtelCollectorExtensions.cs").DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "WithOtelCollectorEnvironment");

        Assert.True(
            wiring.Calls("WaitFor").Count == 0,
            "WithOtelCollectorEnvironment waits for the collector, so a telemetry outage becomes an outage of `userorg` and `gateway`");
    }

    private static CompilationUnitSyntax Root(string fileName)
    {
        var key = Path.Combine("src", "UserOrg.AppHost", fileName);

        Assert.True(
            ResourceCommandSyntax.AllRoots.ContainsKey(key),
            $"expected AppHost source file `{key}` to exist");

        return ResourceCommandSyntax.AllRoots[key];
    }
}
