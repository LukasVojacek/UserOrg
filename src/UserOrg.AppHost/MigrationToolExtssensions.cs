namespace UserOrg.AppHost;

internal static class MigrationToolExtssensions
{
    public static IResourceBuilder<ProjectResource> AddMigrationTools(this IDistributedApplicationBuilder builder,
        IResourceBuilder<SqlServerServerResource> sql,
        IResourceBuilder<SqlServerDatabaseResource> db)
    {
        //var migrationsList = builder.AddExecutable(
        //        name: "db-migrations-list",
        //        command: "dotnet",
        //        workingDirectory: migrationsWorkDir,
        //        args: ["run", "--", "list"])
        //    .WithEnvironment("ConnectionStrings__UserOrgDBContext", db)
        //    .WithEnvironment("ConnectionStrings__ForgeDBContext", db)
        //    .WaitFor(sql)
        //    .WithParentRelationship(sql)
        //    .WithExplicitStart()
        //    .ExcludeFromManifest();

        //var migrationsCheck = builder.AddExecutable(
        //        name: "db-migrations-check",
        //        command: "dotnet",
        //        workingDirectory: migrationsWorkDir,
        //        args: ["run", "--", "check"])
        //    .WithEnvironment("ConnectionStrings__UserOrgDBContext", db)
        //    .WithEnvironment("ConnectionStrings__ForgeDBContext", db)
        //    .WaitFor(sql)
        //    .WithParentRelationship(sql)
        //    .WithExplicitStart()
        //    .ExcludeFromManifest();

        var migrationsApply = builder.AddProject<Projects.DatabaseMigrations>("db-migrations-apply")
            .WithArgs("apply")
            .WithEnvironment("ConnectionStrings__UserOrgDBContext", db)
            .WithEnvironment("ConnectionStrings__ForgeDBContext", db)
            .WithEnvironment("ConnectionStrings__WorkflowDBContext", db)
            .WaitFor(sql)
            .WithParentRelationship(sql)
            // The migrations belong to the local SQL container, which is in the model whatever the
            // DatabaseSource choice turns out to be (T-071 E1). Explicit start is what keeps them from
            // running against an external database: DatabaseSourceSelection starts this resource only when
            // the local container is the chosen source, and the WaitFor(sql) above still orders it after
            // the container is healthy.
            .WithExplicitStart();
        // In the manifest, unlike the other two migration tools: `userorg` and `gateway` both carry
        // WaitForCompletion on this resource, which a published topology writes as a depends_on with
        // service_completed_successfully, and a depends_on naming a service the manifest does not contain
        // is a compose file docker refuses to load. It is also the step a deployed stack genuinely needs -
        // the Gateway's ForgeMigrationStartupService throws on a database with migrations pending.

        var migrationsScript = builder.AddProject<Projects.DatabaseMigrations>("db-migrations-script")
            .WithArgs("script", "--output", "script.sql")
            .WithEnvironment("ConnectionStrings__UserOrgDBContext", db)
            .WithEnvironment("ConnectionStrings__ForgeDBContext", db)
            .WaitFor(sql)
            .WithParentRelationship(sql)
            .WithExplicitStart()
            .ExcludeFromManifest();

        return migrationsApply;
    }

    // db-seed-e2e (T-059-03): the raw-SQL seed the E2E fixture already uses (Tests/E2E/E2E.Fixtures/seed-data.sql),
    // run once through the SEED mode of the same migrations tool so a local `aspire run` can seed prov1/loc1/TESTPRJ
    // without a database backup. Registered unconditionally (T-071 E4): without the HEAppE mock, loc1.Endpoint
    // falls back to the seed script's own original, non-mock endpoint value instead of the mock's.
    private const string NonMockHeappeEndpoint = "http://test-hpc.example.com";

    public static IResourceBuilder<ProjectResource> AddSeedTool(this IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> migrationsApply,
        IResourceBuilder<ProjectResource> gateway,
        IResourceBuilder<SqlServerDatabaseResource> db,
        IResourceBuilder<WireMockServerResource>? heappeMock)
    {
        var seedScriptPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "Tests", "E2E", "E2E.Fixtures", "seed-data.sql"));

        return builder.AddProject<Projects.DatabaseMigrations>("db-seed-e2e")
            .WithArgs(
                "seed",
                "--script", seedScriptPath,
                "--set", "__PROJECT_OWNER_IDP_ID__=a28127fd-1df5-4c56-be7e-f97953a19708",
                "--set", "__PROJECT_MANAGER_IDP_ID__=5a455b66-3abd-4bd1-b37f-80332572608f",
                "--set", "__PROJECT_MEMBER_IDP_ID__=f0e7217d-6c93-4ff7-9b8f-d1c43dd3df0b",
                "--set", "__SEED_OWNER_AS_ADMIN__=true")
            .WithArgs(async context =>
            {
                context.Args.Add("--set");
                if (heappeMock is not null)
                {
                    context.Args.Add(ReferenceExpression.Create($"__HEAPPE_MOCK_URL__={heappeMock.GetEndpoint("http")}"));
                }
                else
                {
                    context.Args.Add($"__HEAPPE_MOCK_URL__={NonMockHeappeEndpoint}");
                }
                await Task.CompletedTask;
            })
            .WithEnvironment("ConnectionStrings__UserOrgDBContext", db)
            .WithEnvironment("ConnectionStrings__ForgeDBContext", db)
            .WithEnvironment("ConnectionStrings__WorkflowDBContext", db)
            .WaitForCompletion(migrationsApply)
            // Orders the seed after the gateway process exists, and no further: the gateway registers no
            // Aspire health check, so WaitFor releases on Running, and RbacSeedHostedService writes the
            // roles after Kestrel is already listening. The real precondition - rows in forge.Roles, without
            // which the script THROWs 50002/50005/50006 - is held by DatabaseSourceSelection.WaitForRbacRolesAsync.
            .WaitFor(gateway)
            .WithParentRelationship(db)
            .WithExplicitStart()
            .ExcludeFromManifest();
    }
}