using System.Globalization;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UserOrg.AppHost;

/// <summary>
/// The database source of a run — the local SQL container or an external connection string — resolved
/// <em>after</em> <c>Build()</c>, when <see cref="RunSourcesDialog"/> has been answered.
/// </summary>
/// <remarks>
/// <para>
/// The dialog is answered at run time, so the answer cannot decide which resources the model contains
/// (T-071 E1). The SQL container, its database, DbGate and the migration executables are therefore always
/// registered and explicit-start; this type reads the answer exactly once and does the two things that
/// depend on it: it hands the choice to the environment callbacks on <c>userorg</c> and <c>gateway</c>,
/// and, when the choice is the local container, it starts the SQL chain and the seed through
/// <see cref="ResourceCommandService"/>.
/// </para>
/// <para>
/// Ordering is unchanged from the build-time branch it replaces: the SQL container starts first, then
/// <c>db-migrations-apply</c> — whose own <c>WaitFor(sql)</c> holds it until SQL is healthy — and the two
/// projects start on their existing <c>WaitForCompletion(db-migrations-apply)</c> edges. On an external
/// connection string <c>db-migrations-apply</c> must not run at all, so it is published as finished and
/// those same edges release immediately.
/// </para>
/// </remarks>
internal sealed class DatabaseSourceSelection(RunSourcesDialog dialog)
{
    /// <summary>The SQL Server container in this app host.</summary>
    public const string LocalContainer = nameof(LocalContainer);

    /// <summary>An existing database reached through the <c>UserorgDev</c> connection string.</summary>
    public const string ExternalConnectionString = nameof(ExternalConnectionString);

    private readonly TaskCompletionSource<bool> _resolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Whether this run wires the projects to the local SQL container. Completes once the choice is in;
    /// safe to await from every resource-start callback, which all run after the choice is resolved.
    /// </summary>
    public Task<bool> UseLocalContainerAsync(CancellationToken cancellationToken) =>
        _resolved.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Resolves the choice in the background and starts the local SQL chain when it is the one chosen.
    /// Call after <c>Build()</c> and before <c>RunAsync()</c>: the dialog is answered while the host runs,
    /// so this cannot be awaited inline.
    /// </summary>
    public void ApplyTo(
        DistributedApplication app,
        IDistributedApplicationBuilderExtensions.Parameters parameters,
        IResourceBuilder<SqlServerServerResource> sql,
        IResourceBuilder<ProjectResource> migrationApply,
        IResourceBuilder<DbGateContainerResource>? dbGate,
        IResourceBuilder<ProjectResource> seedTool,
        IResourceBuilder<SqlServerDatabaseResource> db)
    {
        // `aspire publish` and `aspire deploy` build the model, ask every callback for its values and
        // exit; no orchestrator ever runs, so nothing publishes the resource states the starts below wait
        // for and a start would hang the publish forever. The answer still has to arrive, because the
        // environment callbacks the publisher runs are waiting on it.
        if (app.Services.GetRequiredService<DistributedApplicationExecutionContext>().IsPublishMode)
        {
            // The local container, the same answer the dismissed dialog gives: the published compose
            // carries the SQL container, so a connection string to a database this app host does not own
            // is the wrong one to write into it.
            _resolved.TrySetResult(true);
            return;
        }

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DatabaseSourceSelection>();

        _ = Task.Run(() => ResolveAndStartAsync(app, parameters, sql, migrationApply, dbGate, seedTool, db, logger, lifetime.ApplicationStopping));
    }

    private async Task ResolveAndStartAsync(
        DistributedApplication app,
        IDistributedApplicationBuilderExtensions.Parameters parameters,
        IResourceBuilder<SqlServerServerResource> sql,
        IResourceBuilder<ProjectResource> migrationApply,
        IResourceBuilder<DbGateContainerResource>? dbGate,
        IResourceBuilder<ProjectResource> seedTool,
        IResourceBuilder<SqlServerDatabaseResource> db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();

        try
        {
            var useLocalContainer = (await dialog.AnswersAsync(app.Services, cancellationToken)).UseLocalContainer;
            _resolved.TrySetResult(useLocalContainer);

            if (useLocalContainer)
            {
                logger.LogInformation("Database source: the local SQL container. Starting it, then applying migrations.");

                var commands = app.Services.GetRequiredService<ResourceCommandService>();
                await StartAsync(commands, notifications, sql.Resource, cancellationToken);

                // db-migrations-apply carries WaitFor(sql), so this call returns only once SQL is healthy —
                // the same "SQL healthy, then migrate, then start the projects" order as before.
                await StartAsync(commands, notifications, migrationApply.Resource, cancellationToken);

                if (dbGate is not null)
                {
                    await StartAsync(commands, notifications, dbGate.Resource, cancellationToken);
                }

                // A migrated database with no rows in it answers 403 on every endpoint the gateway maps,
                // because the caller has no dbo.User row to derive a role from. The seed is what makes the
                // run callable, so it runs by itself rather than waiting for someone to find the command.
                await WaitForRbacRolesAsync(db.Resource, logger, cancellationToken);
                await StartAsync(commands, notifications, seedTool.Resource, cancellationToken);
            }
            else
            {
                logger.LogInformation("Database source: the external connection string. The SQL container stays stopped.");
                await ReportSkippedAsync(notifications, migrationApply.Resource, cancellationToken);
                await ShowRemoteDatabaseHostAsync(notifications, parameters, logger, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app host is shutting down before the dialog was answered; nothing left to start.
        }
        catch (Exception exception)
        {
            // Nothing else can resolve this: `userorg` and `gateway` are waiting on the same task and the
            // run cannot continue without an answer, so the developer running the app host has to act.
            _resolved.TrySetException(exception);
            logger.LogError(exception, "Could not resolve the database source; userorg and gateway cannot be wired to a database.");
        }
    }

    /// <summary>
    /// Holds until the Gateway's RBAC import has filled <c>forge.Roles</c>.
    /// </summary>
    /// <remarks>
    /// The seed script resolves every role id out of that table and <c>THROW</c>s 50002/50005/50006 when it
    /// is empty. <c>db-seed-e2e</c> carries <c>WaitFor(gateway)</c>, which is what puts it after the importer
    /// exists - but a project resource is ready the moment its process is up, and RbacSeedHostedService runs
    /// after Kestrel is already listening, so that edge released 12 ms before the first role was written. The
    /// precondition is rows, so rows are what this waits for.
    /// </remarks>
    private static async Task WaitForRbacRolesAsync(
        SqlServerDatabaseResource db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var connectionString = await db.ConnectionStringExpression.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"'{db.Name}' resolved to no connection string.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));

        while (true)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(deadline.Token);

                await using var command = new SqlCommand("SELECT COUNT(1) FROM [forge].[Roles]", connection);
                var roles = Convert.ToInt32(await command.ExecuteScalarAsync(deadline.Token), CultureInfo.InvariantCulture);

                if (roles > 0)
                {
                    return;
                }
            }
            catch (SqlException)
            {
                // The table itself arrives with the migrations and the gateway may still be opening its own
                // connections; neither is a reason to give up before the deadline.
            }

            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "forge.Roles is still empty two minutes after the gateway started; the seed would fail with 50002/50005/50006, so it was not started. Check the gateway's RBAC import.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
        }
    }

    private static async Task StartAsync(
        ResourceCommandService commands,
        ResourceNotificationService notifications,
        IResource resource,
        CancellationToken cancellationToken)
    {
        // A choice answered from a user secret arrives before the orchestrator has finished registering the
        // explicit-start resources; NotStarted is the state that says "registered, waiting to be started".
        await notifications.WaitForResourceAsync(resource.Name, KnownResourceStates.NotStarted, cancellationToken);

        var result = await commands.ExecuteCommandAsync(resource, KnownResourceCommands.StartCommand, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidOperationException($"Could not start '{resource.Name}': {result.Message}");
        }
    }

    /// <summary>
    /// Releases the <c>WaitForCompletion(db-migrations-apply)</c> edges without running the migrations:
    /// an external database is migrated by whoever owns it, not by this app host.
    /// </summary>
    private static async Task ReportSkippedAsync(
        ResourceNotificationService notifications,
        ProjectResource migrationApply,
        CancellationToken cancellationToken)
    {
        // NotStarted is the last state the orchestrator publishes for an explicit-start resource. Publishing
        // before it lands is a race the orchestrator wins, and the two projects then wait for a resource that
        // is never going to run.
        await notifications.WaitForResourceAsync(migrationApply.Name, KnownResourceStates.NotStarted, cancellationToken);

        await notifications.PublishUpdateAsync(migrationApply, snapshot => snapshot with
        {
            State = new ResourceStateSnapshot(KnownResourceStates.Finished, KnownResourceStateStyles.Success),
            ExitCode = 0
        });
    }

    private static async Task ShowRemoteDatabaseHostAsync(
        ResourceNotificationService notifications,
        IDistributedApplicationBuilderExtensions.Parameters parameters,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var connectionString = await parameters.UserorgDev.Resource.GetValueAsync(cancellationToken);
        var url = RemoteDatabaseUrl.FromConnectionString(connectionString);
        var remoteDatabase = parameters.RemoteDatabaseHost.Resource;

        if (url is null)
        {
            logger.LogWarning("The UserorgDev connection string names no TCP host, so '{Resource}' keeps its RemoteDatabaseHost URL.", remoteDatabase.Name);
            return;
        }

        await notifications.WaitForResourceAsync(remoteDatabase.Name, KnownResourceStates.Running, cancellationToken);

        await notifications.PublishUpdateAsync(remoteDatabase, snapshot => snapshot with
        {
            Urls = [.. snapshot.Urls.Select(existing => existing with { Url = url })]
        });
    }
}