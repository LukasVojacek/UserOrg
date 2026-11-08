namespace UserOrg.AppHost;

internal static class ImmuDbExtensions
{
    // The audit archive's login. It travels sealed inside every envelope the producers spool, so
    // the init container and the producers have to name the same one.
    internal const string AuditUser = "immudb";
    internal const string AuditPassword = "immudb";
    internal const string AuditDatabase = "defaultdb";

    private static string InitScriptContents { get; } = VaultSetup.ReadEmbeddedText("create_audit_log_table.sh");

    public static (IResourceBuilder<ContainerResource> immuGw, IResourceBuilder<ContainerResource> auditLogTable) AddImmuDb(this IDistributedApplicationBuilder builder)
    {
        var immudb = builder.AddContainer("immudb", "codenotary/immudb")
            .WithImageTag("1.11.2")
            .WithLifetime(ContainerLifetime.Persistent)
            .WithEndpoint(targetPort: 3322, scheme: "tcp")
            .WithHttpEndpoint(targetPort: 8080)
            // A running immudb container is not a ready one: immugw can exit on start while immudb's
            // gRPC port is not answering yet, so WaitFor(immudb) has to gate on /readyz.
            .WithHttpEndpoint(targetPort: 9497, name: "metrics")
            .WithHttpHealthCheck("/readyz", 200, "metrics")
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "📜 immudb console";
                url.DisplayLocation = UrlDisplayLocation.SummaryAndDetails;
            })
            .WithUrlForEndpoint("tcp", static url =>
            {
                url.DisplayText = "🔌 immudb gRPC";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            });

        var immuGw = builder.AddContainer("immugw", "codenotary/immugw")
            .WithImageTag("1.3.1")
            .WithLifetime(ContainerLifetime.Persistent)
            .WaitFor(immudb)
            .WithContainerRuntimeArgs("--restart", "on-failure:5")
            .WithEnvironment("IMMUGW_IMMUDB_ADDRESS", "immudb")
            .WithEnvironment("IMMUGW_IMMUDB_PORT", "3322")
            .WithHttpEndpoint(targetPort: 3323)
            .WithUrlForEndpoint("http", static url =>
            {
                url.DisplayText = "🔗 immudb REST gateway";
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            })
            .WithParentRelationship(immudb)
            .WithExternalHttpEndpoints()
            .WithReferenceRelationship(immudb);

        // The archive's schema is created here rather than by a producer or by the unsealing proxy:
        // the proxy forwards sealed statements without knowing a single column name, and a producer
        // that could create the table could also alter it.
        var auditLogTable = builder.AddContainer("immudb-audit-log-table", "curlimages/curl")
            .WithImageTag("8.22.0")
            .WithLifetime(ContainerLifetime.Session)
            .WaitFor(immuGw)
            .WithContainerFiles(
                "/scripts",
                [
                    new ContainerFile
                    {
                        Name = "create_audit_log_table.sh",
                        Contents = InitScriptContents.AsContainerFileContents(builder)
                    }
                ])
            .WithEntrypoint("sh")
            .WithArgs("/scripts/create_audit_log_table.sh")
            .WithEnvironment("IMMUGW_URL", immuGw.GetEndpoint("http"))
            .WithEnvironment("IMMUDB_USER", AuditUser)
            .WithEnvironment("IMMUDB_PASSWORD", AuditPassword)
            .WithEnvironment("IMMUDB_DATABASE", AuditDatabase)
            .WithParentRelationship(immudb);

        return (immuGw, auditLogTable);
    }
}