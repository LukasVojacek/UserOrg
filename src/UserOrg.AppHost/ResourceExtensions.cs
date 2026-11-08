using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace UserOrg.AppHost;

internal static class ResourceExtensions
{
    /// <summary>
    /// Names a resource's URLs and moves them out of the dashboard's summary into its details. The
    /// external services need this rather than <c>WithUrlForEndpoint</c>: their URL comes from a
    /// parameter, not from an endpoint, so there is no endpoint name to address it by. They are
    /// addresses the app host calls, not pages anyone opens, so the summary row stays for the
    /// resources that really do serve a UI.
    /// </summary>
    internal static IResourceBuilder<T> WithDetailsOnlyUrls<T>(this IResourceBuilder<T> builder, string displayText)
        where T : IResource =>
        builder.WithUrls(context =>
        {
            foreach (var url in context.Urls)
            {
                url.DisplayText = displayText;
                url.DisplayLocation = UrlDisplayLocation.DetailsOnly;
            }

            return Task.CompletedTask;
        });

    extension(IResourceBuilder<SqlServerServerResource> dbContainer)
    {
        /// <summary>
        /// Adds two dashboard commands to the SQL Server resource for fast local development
        /// iteration: <c>Snapshot</c> captures the current state of <paramref name="targetDatabaseName"/>
        /// as a SQL Server database snapshot, and <c>Revert to snapshot</c> restores it. Requires an
        /// edition that supports database snapshots (Developer/Enterprise). This mirrors the
        /// programmatic reset the E2E fixture uses (ADR-0052); here it is a manual dev convenience.
        /// </summary>
        public IResourceBuilder<SqlServerServerResource> WithSnapshotCommands(string targetDatabaseName)
        {
            var snapshotName = $"{targetDatabaseName}_dev_snapshot";

            var createSnapshotSql = $"""
                IF DB_ID(N'{snapshotName}') IS NOT NULL
                    DROP DATABASE [{snapshotName}];

                DECLARE @logicalName sysname, @physical nvarchar(4000), @ssFile nvarchar(4000), @sql nvarchar(max);
                SELECT TOP 1 @logicalName = name, @physical = physical_name
                FROM sys.master_files
                WHERE database_id = DB_ID(N'{targetDatabaseName}') AND type = 0;

                SET @ssFile = REPLACE(@physical, '.mdf', '_devsnap.ss');
                SET @sql = N'CREATE DATABASE [{snapshotName}] ON (NAME = ' + QUOTENAME(@logicalName)
                    + N', FILENAME = ''' + @ssFile + N''') AS SNAPSHOT OF [{targetDatabaseName}];';
                EXEC sp_executesql @sql;
                """;

            var revertSql = $"""
                IF DB_ID(N'{snapshotName}') IS NULL
                    THROW 50000, 'No snapshot exists. Run the Snapshot command first.', 1;

                ALTER DATABASE [{targetDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                RESTORE DATABASE [{targetDatabaseName}] FROM DATABASE_SNAPSHOT = N'{snapshotName}';
                ALTER DATABASE [{targetDatabaseName}] SET MULTI_USER;
                """;

            return dbContainer
                .WithCommand(
                    name: "SnapshotDatabase",
                    displayName: " Snapshot",
                    commandOptions: new CommandOptions
                    {
                        UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                            ? ResourceCommandState.Enabled
                            : ResourceCommandState.Disabled,
                        IconName = "Camera",
                        IconVariant = IconVariant.Filled,
                        ConfirmationMessage = $"Capture a snapshot of {targetDatabaseName}? This replaces any previous dev snapshot."
                    },
                    executeCommand: context => ExecuteSnapshotSqlAsync(dbContainer, createSnapshotSql, context.CancellationToken))
                .WithCommand(
                    name: "RevertToSnapshot",
                    displayName: " Revert to snapshot",
                    commandOptions: new CommandOptions
                    {
                        UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                            ? ResourceCommandState.Enabled
                            : ResourceCommandState.Disabled,
                        IconName = "ArrowUndo",
                        IconVariant = IconVariant.Filled,
                        ConfirmationMessage = $"Revert {targetDatabaseName} to the last snapshot? All changes since the snapshot are lost."
                    },
                    executeCommand: context => ExecuteSnapshotSqlAsync(dbContainer, revertSql, context.CancellationToken));
        }

        public IResourceBuilder<SqlServerServerResource> WithDatabaseLifecycleCommand(
            string targetDatabaseName,
            IResourceBuilder<ProjectResource> migrationsApply,
            IResourceBuilder<ProjectResource> seedTool)
        {
            var resetSql = $"""
                ALTER DATABASE [{targetDatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{targetDatabaseName}];
                CREATE DATABASE [{targetDatabaseName}];
                """;

            return dbContainer.WithCommand(
                name: "db-lifecycle",
                displayName: "Database lifecycle",
                commandOptions: new CommandOptions
                {
                    Arguments =
                    [
                        new InteractionInput
                        {
                            Name = "Mode",
                            Label = "Mode",
                            InputType = InputType.Choice,
                            Required = true,
                            Options =
                            [
                                new("Reset", "Reset"),
                                new("ApplyMigrations", "Apply migrations"),
                                new("Seed", "Seed"),
                                new("ResetAndSeed", "Reset and seed"),
                            ]
                        }
                    ],
                    UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                        ? ResourceCommandState.Enabled
                        : ResourceCommandState.Disabled,
                    IconName = "ArrowSync",
                    IconVariant = IconVariant.Filled
                },
                executeCommand: context => ExecuteDatabaseLifecycleAsync(
                    dbContainer, targetDatabaseName, resetSql, migrationsApply, seedTool, context));
        }

        public IResourceBuilder<SqlServerServerResource> WithDatabaseRestoreCommand(
            IResourceBuilder<ParameterResource> backupFilePath,
            IResourceBuilder<ParameterResource> databaseName,
            string? targetDatabaseName = null)
        {
            // targetDatabaseName is the name of the database to restore into.
            // databaseName is the logical file name used inside the backup (MOVE clause).
            // backupFilePath is a parameter that will be resolved at runtime.
            var restoreTarget = targetDatabaseName ?? databaseName.Resource.Name;
            var containerBackupPath = "/var/opt/mssql/backup/";

            return dbContainer
                .WithBindMount("dbbackup/", containerBackupPath)
                .WithCommand(
                    name: "RestoreDevDatabase",
                    displayName: " Restore",
                    commandOptions: new CommandOptions()
                    {
                        UpdateState = context => context.ResourceSnapshot.HealthStatus is HealthStatus.Healthy
                            ? ResourceCommandState.Enabled
                            : ResourceCommandState.Disabled,
                        IconName = "ArchiveArrowBack",
                        IconVariant = IconVariant.Filled,
                        ConfirmationMessage = $"Are you sure you want to restore the database? This will overwrite the existing {restoreTarget} database if it exists."
                    },
                    executeCommand: async (context) =>
                    {
                        var logger = context.ServiceProvider.GetRequiredService<ILogger<Program>>();
                        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
                        var cancellationToken = context.CancellationToken;

                        // Get parameter values at runtime (declare before try so they're accessible in catch)
                        var backupFilePathValue = Path.GetFileName(await backupFilePath.Resource.GetValueAsync(cancellationToken));
                        var databaseNameValue = await databaseName.Resource.GetValueAsync(cancellationToken);

                        async Task<ExecuteCommandResult> RunRestoreAsync()
                        {
                            try
                            {

                                if (interactionService.IsAvailable)
                                {
                                    interactionService.PromptNotificationAsync(
                                        title: "Database Restore",
                                        message: $"Starting database restore operation for {restoreTarget}...",
                                        options: new NotificationInteractionOptions
                                        {
                                            Intent = MessageIntent.Information
                                        },
                                        cancellationToken: cancellationToken);
                                }

                                var connectionString = await dbContainer.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);

                                await using var connection = new SqlConnection(connectionString);
                                await connection.OpenAsync(cancellationToken);

                                // Check if database exists and set to single-user mode to close all connections
                                var prepareDatabaseScript = $"""
                              IF EXISTS (SELECT name FROM sys.databases WHERE name = '{restoreTarget}')
                                 BEGIN
                                       ALTER DATABASE [{restoreTarget}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                                 END
                              """;

                                await using (var prepareCommand = new SqlCommand(prepareDatabaseScript, connection))
                                {
                                    await prepareCommand.ExecuteNonQueryAsync(cancellationToken);
                                }

                                // SQL script to restore the database.
                                // databaseNameValue refers to the logical file names inside the backup (MOVE clause).
                                // restoreTarget is the name of the target database to restore into.
                                var restoreScript = $"""
                             RESTORE DATABASE [{restoreTarget}]
                              FROM DISK = N'{containerBackupPath}{backupFilePathValue}'
                              WITH MOVE '{databaseNameValue}' TO '/var/opt/mssql/data/{restoreTarget}.mdf',
                                   MOVE '{databaseNameValue}_log' TO '/var/opt/mssql/data/{restoreTarget}_log.ldf',
                              REPLACE
                             """;

                                await using (var command = new SqlCommand(restoreScript, connection)
                                {
                                    CommandTimeout = 300 // 5 minutes timeout for restore operation
                                })
                                {
                                    await command.ExecuteNonQueryAsync(cancellationToken);
                                }

                                var setMultiUserScript = $"ALTER DATABASE [{restoreTarget}] SET MULTI_USER;";
                                await using (var multiUserCommand = new SqlCommand(setMultiUserScript, connection))
                                {
                                    await multiUserCommand.ExecuteNonQueryAsync(cancellationToken);
                                }

                                if (interactionService.IsAvailable)
                                {
                                    interactionService.PromptNotificationAsync(
                                      title: "Database Restore",
                                      message: $"✅ Database restore completed successfully! The {restoreTarget} database has been restored and is ready to use.",
                                      options: new NotificationInteractionOptions
                                      {
                                          Intent = MessageIntent.Success
                                      },
                                      cancellationToken: cancellationToken);
                                }

                                return CommandResults.Success();
                            }
                            catch (SqlException ex)
                            {
                                if (interactionService.IsAvailable)
                                {
                                    interactionService.PromptNotificationAsync(
                                      title: "Database Restore Failed",
                                      message: $"❌ Failed to restore database {restoreTarget}: {ex.Message}",
                                      options: new NotificationInteractionOptions
                                      {
                                          Intent = MessageIntent.Error
                                      },
                                      cancellationToken: cancellationToken);
                                }

                                // If error suggests using RESTORE FILELISTONLY, run it and show results to help the user
                                try
                                {
                                    var msg = ex.Message ?? string.Empty;
                                    if (msg.Contains("RESTORE FILELISTONLY", StringComparison.OrdinalIgnoreCase)
                                        || msg.Contains("Use RESTORE FILELISTONLY", StringComparison.OrdinalIgnoreCase)
                                        || msg.Contains("Logical file", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var connectionString = await dbContainer.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
                                        await using var connection = new SqlConnection(connectionString);
                                        await connection.OpenAsync(cancellationToken);

                                        var fileListCommandText = $"RESTORE FILELISTONLY FROM DISK = N'{containerBackupPath}{backupFilePathValue}'";
                                        await using var fileListCommand = new SqlCommand(fileListCommandText, connection)
                                        {
                                            CommandTimeout = 60
                                        };

                                        await using var reader = await fileListCommand.ExecuteReaderAsync(cancellationToken);
                                        var sb = new StringBuilder();
                                        sb.AppendLine("Suggested logical file names from backup:");

                                        static string? GetStringByName(SqlDataReader r, string name)
                                        {
                                            try
                                            {
                                                var idx = r.GetOrdinal(name);
                                                if (r.IsDBNull(idx))
                                                    return null;
                                                var v = r.GetValue(idx);
                                                return v?.ToString();
                                            }
                                            catch (IndexOutOfRangeException)
                                            {
                                                return null;
                                            }
                                        }

                                        while (await reader.ReadAsync(cancellationToken))
                                        {
                                            string? logicalName = GetStringByName(reader, "LogicalName");
                                            string? physicalName = GetStringByName(reader, "PhysicalName");
                                            if (!string.IsNullOrEmpty(logicalName))
                                            {
                                                sb.Append($"- {logicalName}");
                                                if (!string.IsNullOrEmpty(physicalName))
                                                {
                                                    sb.Append($" (physical: {physicalName})");
                                                }
                                                sb.AppendLine();
                                            }
                                            else
                                            {
                                                for (int i = 0; i < reader.FieldCount; i++)
                                                {
                                                    sb.Append(reader.GetName(i));
                                                    sb.Append(": ");
                                                    sb.Append(reader.IsDBNull(i) ? "<null>" : reader.GetValue(i)?.ToString());
                                                    if (i < reader.FieldCount - 1)
                                                        sb.Append("; ");
                                                }
                                                sb.AppendLine();
                                            }
                                        }

                                        var fileListMessage = sb.ToString();
                                        if (interactionService.IsAvailable)
                                        {
                                            interactionService.PromptNotificationAsync(
                                                title: "Restore - Backup file contents",
                                                message: fileListMessage,
                                                options: new NotificationInteractionOptions { Intent = MessageIntent.Information },
                                                cancellationToken: cancellationToken);
                                        }
                                    }
                                }
                                catch (Exception listEx)
                                {
                                    // Log and continue; listing file contents is best-effort helper for the user
                                    logger.LogDebug(listEx, "Failed to run RESTORE FILELISTONLY while handling restore error for {DatabaseName}", restoreTarget);
                                }

                                // Try to set database back to multi-user mode in case of failure
                                try
                                {
                                    var connectionString = await dbContainer.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
                                    await using var connection = new SqlConnection(connectionString);
                                    await connection.OpenAsync(cancellationToken);
                                    await using var command = new SqlCommand($"IF EXISTS (SELECT name FROM sys.databases WHERE name = '{restoreTarget}') ALTER DATABASE [{restoreTarget}] SET MULTI_USER;", connection);
                                    await command.ExecuteNonQueryAsync(cancellationToken);
                                }
                                catch (SqlException)
                                {
                                    // Ignore SQL errors during cleanup
                                }
                                catch (InvalidOperationException)
                                {
                                    // Ignore connection errors during cleanup
                                }

                                return CommandResults.Failure(ex);
                            }
                            catch (InvalidOperationException ex)
                            {
                                logger.LogError(ex, "Invalid operation during database restore for {DatabaseName}", restoreTarget);

                                if (interactionService.IsAvailable)
                                {
                                    interactionService.PromptNotificationAsync(
                                      title: "Database Restore Failed",
                                      message: $"❌ Failed to restore database {restoreTarget}: {ex.Message}",
                                      options: new NotificationInteractionOptions
                                      {
                                          Intent = MessageIntent.Error
                                      },
                                      cancellationToken: cancellationToken);
                                }

                                return CommandResults.Failure(ex);
                            }
                        }

                        if (interactionService.IsAvailable)
                        {
                            var result = CommandResults.Failure("The restore did not run.");
                            await interactionService.PromptProgressAsync(
                                $"Restoring {restoreTarget}...",
                                new ProgressInteractionOptions
                                {
                                    Work = async _ => { result = await RunRestoreAsync(); }
                                },
                                cancellationToken);
                            return result;
                        }

                        return await RunRestoreAsync();
                    }
                );
        }
    }

    private static async Task<ExecuteCommandResult> ExecuteSnapshotSqlAsync(
        IResourceBuilder<SqlServerServerResource> dbContainer,
        string sql,
        CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = await dbContainer.Resource.ConnectionStringExpression.GetValueAsync(cancellationToken);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync(cancellationToken);
            return CommandResults.Success();
        }
        catch (SqlException ex)
        {
            return CommandResults.Failure(ex);
        }
    }

    private static async Task<ExecuteCommandResult> ExecuteDatabaseLifecycleAsync(
        IResourceBuilder<SqlServerServerResource> dbContainer,
        string targetDatabaseName,
        string resetSql,
        IResourceBuilder<ProjectResource> migrationsApply,
        IResourceBuilder<ProjectResource> seedTool,
        ExecuteCommandContext context)
    {
        var cancellationToken = context.CancellationToken;
        var mode = context.Arguments["Mode"].Value ?? string.Empty;
        var interactionService = context.ServiceProvider.GetRequiredService<IInteractionService>();
        var commands = context.ServiceProvider.GetRequiredService<ResourceCommandService>();
        var notifications = context.ServiceProvider.GetRequiredService<ResourceNotificationService>();

        if (interactionService.IsAvailable && mode is "Reset" or "ResetAndSeed")
        {
            var confirmation = await interactionService.PromptConfirmationAsync(
                title: "Reset the database",
                message: $"This resets {targetDatabaseName}: it drops the database and creates it again. Are you sure you want to reset?",
                options: new MessageBoxInteractionOptions { Intent = MessageIntent.Warning },
                cancellationToken: cancellationToken);

            if (confirmation.Canceled || !confirmation.Data)
            {
                return CommandResults.Canceled();
            }
        }

        async Task<ExecuteCommandResult> RunAsync() => mode switch
        {
            "Reset" => await ExecuteSnapshotSqlAsync(dbContainer, resetSql, cancellationToken),
            "ApplyMigrations" => await RunToCompletionAsync(
                commands, notifications, migrationsApply, "apply migrations", cancellationToken),
            "Seed" => await RunToCompletionAsync(
                commands, notifications, seedTool, "seed the database", cancellationToken),
            "ResetAndSeed" => await ResetAndSeedAsync(
                dbContainer, resetSql, migrationsApply, seedTool, commands, notifications, cancellationToken),
            _ => CommandResults.Failure($"Unknown db-lifecycle mode '{mode}'.")
        };

        ExecuteCommandResult result;
        if (interactionService.IsAvailable)
        {
            result = CommandResults.Failure("The lifecycle command did not run.");
            await interactionService.PromptProgressAsync(
                $"Running {mode} on {targetDatabaseName}...",
                new ProgressInteractionOptions
                {
                    Work = async _ => { result = await RunAsync(); }
                },
                cancellationToken);
        }
        else
        {
            result = await RunAsync();
        }

        if (!result.Success)
        {
            return result;
        }

        return CommandResults.Success(
            $"db-lifecycle: {mode} completed for {targetDatabaseName}",
            $"Ran {mode} against {targetDatabaseName}.",
            CommandResultFormat.Text);
    }

    private static async Task<ExecuteCommandResult> RunToCompletionAsync(
        ResourceCommandService commands,
        ResourceNotificationService notifications,
        IResourceBuilder<ProjectResource> tool,
        string what,
        CancellationToken cancellationToken)
    {
        notifications.TryGetCurrentState(tool.Resource.Name, out var baseline);
        var baselineStart = baseline?.Snapshot.StartTimeStamp;

        var start = await commands.ExecuteCommandAsync(tool.Resource, KnownResourceCommands.StartCommand, cancellationToken);
        if (!start.Success)
        {
            return CommandResults.Failure($"Could not {what}: {start.Message}");
        }

        var final = await notifications.WaitForResourceAsync(
            tool.Resource.Name,
            evt => KnownResourceStates.TerminalStates.Contains(evt.Snapshot.State?.Text ?? string.Empty)
                && (baselineStart is null || evt.Snapshot.StartTimeStamp > baselineStart),
            cancellationToken);

        if (final.Snapshot.State?.Text == KnownResourceStates.FailedToStart)
        {
            return CommandResults.Failure($"Could not {what}: {tool.Resource.Name} failed to start.");
        }

        if (final.Snapshot.ExitCode is > 0)
        {
            return CommandResults.Failure($"Could not {what}: {tool.Resource.Name} exited with code {final.Snapshot.ExitCode}.");
        }

        return CommandResults.Success();
    }

    private static async Task<ExecuteCommandResult> ResetAndSeedAsync(
        IResourceBuilder<SqlServerServerResource> dbContainer,
        string resetSql,
        IResourceBuilder<ProjectResource> migrationsApply,
        IResourceBuilder<ProjectResource> seedTool,
        ResourceCommandService commands,
        ResourceNotificationService notifications,
        CancellationToken cancellationToken)
    {
        var reset = await ExecuteSnapshotSqlAsync(dbContainer, resetSql, cancellationToken);
        if (!reset.Success)
        {
            return reset;
        }

        var migrate = await RunToCompletionAsync(
            commands, notifications, migrationsApply, "apply migrations", cancellationToken);
        if (!migrate.Success)
        {
            return migrate;
        }

        return await RunToCompletionAsync(
            commands, notifications, seedTool, "seed the database", cancellationToken);
    }

    /// <summary>
    /// The contents of a file delivered by <c>WithContainerFiles</c>, safe for the mode this app host is
    /// running in.
    /// </summary>
    /// <remarks>
    /// Under <c>aspire run</c> the bytes are written into the container as they are. Under
    /// <c>aspire publish</c> they are written into the compose file as a <c>configs</c> entry, and docker
    /// compose interpolates <c>$</c> in that content: an OpenTelemetry collector's
    /// <c>${env:OTEL_EXPORTER_OTLP_ENDPOINT}</c> is rejected outright as an invalid interpolation, and a
    /// shell script's <c>$IMMUDB_USER</c> is quietly replaced with an empty string. Doubling the sigil is
    /// how compose is told to leave it alone; it unescapes back to one <c>$</c> in the container.
    /// </remarks>
    internal static string AsContainerFileContents(this string contents, IDistributedApplicationBuilder builder) =>
        builder.ExecutionContext.IsPublishMode ? contents.Replace("$", "$$", StringComparison.Ordinal) : contents;
}