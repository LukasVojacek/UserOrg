using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace UserOrg.Tools.DatabaseMigrations;
#pragma warning disable CA1031

internal sealed class MigrationRunner
{
    private readonly IServiceProvider _serviceProvider;

    public MigrationRunner(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// List all DbContexts and their migration status
    /// </summary>
    public async Task<int> ListMigrationsAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]Database Migration Status[/]");
        AnsiConsole.WriteLine();

        List<Type> contextTypes = GetAllDbContextTypes();

        foreach (Type contextType in contextTypes)
        {
            try
            {
                DbContext? context = _serviceProvider.GetService(contextType) as DbContext;
                if (context == null)
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠[/] Unable to resolve [cyan]{contextType.Name}[/]");
                    continue;
                }

                IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                IEnumerable<string> appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
                IEnumerable<string> allMigrations = context.Database.GetMigrations();

                var table = new Table()
                    .Border(TableBorder.Rounded)
                    .Title($"[bold]{contextType.Name}[/]")
                    .AddColumn("[bold]Metric[/]")
                    .AddColumn("[bold]Value[/]");

                table.AddRow("Total Migrations", $"[cyan]{allMigrations.Count()}[/]");
                table.AddRow("Applied", $"[green]{appliedMigrations.Count()}[/]");
                table.AddRow("Pending", pendingMigrations.Any() ? $"[yellow]{pendingMigrations.Count()}[/]" : $"[green]{pendingMigrations.Count()}[/]");

                AnsiConsole.Write(table);

                if (pendingMigrations.Any())
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[yellow]Pending migrations:[/]");
                    foreach (string migration in pendingMigrations)
                    {
                        AnsiConsole.MarkupLine($"  [yellow]•[/] {migration}");
                    }
                }

                AnsiConsole.WriteLine();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Error processing [cyan]{contextType.Name}[/]");
                AnsiConsole.WriteLine(ex.Message);
                AnsiConsole.WriteLine();
            }
        }

        return 0;
    }

    /// <summary>
    /// Check if all migrations are applied; exit code 1 if any pending migrations exist
    /// </summary>
    public async Task<int> CheckMigrationsAsync()
    {
        AnsiConsole.MarkupLine("[bold blue]Checking Migration Status[/]");
        AnsiConsole.WriteLine();

        List<Type> contextTypes = GetAllDbContextTypes();
        bool hasPending = false;
        bool hasErrors = false;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]DbContext[/]")
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Pending[/]");

        foreach (Type contextType in contextTypes)
        {
            try
            {
                DbContext? context = _serviceProvider.GetService(contextType) as DbContext;
                if (context == null)
                {
                    table.AddRow(
                        contextType.Name,
                        "[yellow]⚠ Unable to resolve[/]",
                        "-");
                    hasErrors = true;
                    continue;
                }

                IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (pendingMigrations.Any())
                {
                    table.AddRow(
                        contextType.Name,
                        "[yellow]⚠ Pending[/]",
                        $"[yellow]{pendingMigrations.Count()}[/]");
                    hasPending = true;
                }
                else
                {
                    table.AddRow(
                        contextType.Name,
                        "[green]✓ Up to date[/]",
                        "[green]0[/]");
                }
            }
            catch (Exception ex)
            {
                table.AddRow(
                    contextType.Name,
                    "[red]✗ Error[/]",
                    "-");
                AnsiConsole.MarkupLine("[red]Error:[/]");
                AnsiConsole.WriteLine(ex.Message);
                hasErrors = true;
            }
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (hasErrors)
        {
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Errors occurred during migration check[/]");
            return 1;
        }

        if (hasPending)
        {
            AnsiConsole.MarkupLine("[yellow]⚠[/] [bold]One or more DbContexts have pending migrations[/]");
            return 1;
        }

        AnsiConsole.MarkupLine("[green]✓[/] [bold]All DbContexts are up to date[/]");
        return 0;
    }

    /// <summary>
    /// Generate idempotent SQL script for all or specified DbContexts using dotnet ef CLI
    /// This method does not require a database connection
    /// </summary>
    public async Task<int> GenerateScriptAsync(string[] args)
    {
        string? outputPath = null;
        List<string>? contextFilter = null;

        // Parse arguments: --output <path> [--contexts Ctx1,Ctx2]
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--output" && i + 1 < args.Length)
            {
                outputPath = args[++i];
            }
            else if (args[i] == "--contexts" && i + 1 < args.Length)
            {
                contextFilter = [.. args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Missing required argument[/]");
            AnsiConsole.MarkupLine("[yellow]Usage:[/] DatabaseMigrations script --output <path>");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Generating SQL Migration Script[/]");
        AnsiConsole.MarkupLine("[dim]Generating SQL scripts in-process via IMigrator (no external dotnet-ef required)[/]");
        AnsiConsole.WriteLine();

        Dictionary<Type, string> contextProjects = new()
        {
            { typeof(Core.DataAccess.Contexts.DatabaseContext), "src/Modules/Core.DataAccess/Core.DataAccess.csproj" },
            { typeof(Forge.Core.Persistence.ForgeDbContext), "src/Modules/Libraries/Forge.Core/Forge.Core.csproj" },
            { typeof(Heappe.Persistence.HeappeIntegrationDbContext), "src/Modules/Integrations/Heappe/Heappe.csproj" },
            { typeof(Core.DataAccess.Outbox.OutboxDbContext), "src/Modules/Core.DataAccess/Core.DataAccess.csproj" },
            { typeof(Core.DataAccess.ResourceRequests.ResourceRequestsDbContext), "src/Modules/Core.DataAccess/Core.DataAccess.csproj" },
            { typeof(WorkflowEngine.Core.Persistence.WorkflowDbContext), "src/Modules/Libraries/WorkflowEngine.Core/WorkflowEngine.Core.csproj" }
        };

        List<Type> contextTypes = GetAllDbContextTypes();
        if (contextFilter != null && contextFilter.Count > 0)
        {
            contextTypes = contextTypes.Where(t => contextFilter.Contains(t.Name)).ToList();
            AnsiConsole.MarkupLine($"[dim]Filtering to contexts:[/] {string.Join(", ", contextFilter)}");
        }

        if (contextTypes.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] No DbContexts found matching filter");
            return 1;
        }

        StringBuilder scriptBuilder = new();
        scriptBuilder.AppendLine("-- UserOrg Database Migrations - Idempotent SQL Script");
        scriptBuilder.AppendLine($"-- Generated: {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} UTC");
        scriptBuilder.AppendLine("-- This script is idempotent and can be run multiple times safely");
        scriptBuilder.AppendLine("-- Generated using: dotnet ef migrations script --idempotent");
        scriptBuilder.AppendLine();

        bool hasErrors = false;

        foreach (Type contextType in contextTypes)
        {
            if (!contextProjects.TryGetValue(contextType, out string? projectPath))
            {
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Unknown project path for [cyan]{contextType.Name}[/]");
                continue;
            }

            AnsiConsole.MarkupLine($"Processing [cyan]{contextType.Name}[/]...");

            string currentDir = AppContext.BaseDirectory;
            string repoRoot = currentDir;

            while (!string.IsNullOrEmpty(repoRoot))
            {
                if (Directory.Exists(Path.Combine(repoRoot, ".git")) ||
                    Directory.GetFiles(repoRoot, "*.sln").Length > 0)
                {
                    break;
                }

                string? parent = Directory.GetParent(repoRoot)?.FullName;
                if (parent == null)
                {
                    // Fallback: assume we're in artifacts/bin/DatabaseMigrations/debug
                    repoRoot = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", ".."));
                    break;
                }
                repoRoot = parent;
            }

            string fullProjectPath = Path.Combine(repoRoot, projectPath);

            if (!File.Exists(fullProjectPath))
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Project file not found: [dim]{fullProjectPath}[/]");
                hasErrors = true;
                continue;
            }

            try
            {
                // Try generating script in-process using EF Core IMigrator to avoid relying on external dotnet ef
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var scopedProvider = scope.ServiceProvider;
                    var context = scopedProvider.GetService(contextType) as DbContext;

                    if (context == null)
                    {
                        AnsiConsole.MarkupLine($"[yellow]⚠[/] Unable to resolve [cyan]{contextType.Name}[/] for script generation");
                        hasErrors = true;
                        continue;
                    }

                    var migrator = context.Database.GetService<IMigrator>();
                    if (migrator == null)
                    {
                        AnsiConsole.MarkupLine($"[red]✗[/] IMigrator not available for [cyan]{contextType.Name}[/]");
                        hasErrors = true;
                        continue;
                    }

                    string? sql = null;
                    try
                    {
                        var migratorType = migrator.GetType();

                        // Try synchronous GenerateScript first (most implementations expose this)
                        var genSync = migratorType.GetMethod("GenerateScript", BindingFlags.Public | BindingFlags.Instance);
                        if (genSync != null)
                        {
                            var p = genSync.GetParameters();
                            object?[] genArgs = new object?[p.Length];
                            for (int i = 0; i < p.Length; i++)
                            {
                                var pt = p[i].ParameterType;
                                if (pt == typeof(bool))
                                {
                                    genArgs[i] = true;
                                }
                                else if (pt.IsEnum)
                                {
                                    // Try to find an 'Idempotent' enum value if the API changed to use enum options
                                    var enumNames = Enum.GetNames(pt);
                                    string? match = Array.Find(enumNames, n => n.Contains("idempot", StringComparison.OrdinalIgnoreCase));
                                    if (match != null)
                                    {
                                        genArgs[i] = Enum.Parse(pt, match);
                                    }
                                    else if (p[i].HasDefaultValue)
                                    {
                                        genArgs[i] = p[i].DefaultValue;
                                    }
                                    else
                                    {
                                        genArgs[i] = Activator.CreateInstance(pt);
                                    }
                                }
                                else if (pt == typeof(CancellationToken))
                                {
                                    genArgs[i] = CancellationToken.None;
                                }
                                else if (pt == typeof(string))
                                {
                                    genArgs[i] = null;
                                }
                                else if (p[i].HasDefaultValue)
                                {
                                    genArgs[i] = p[i].DefaultValue;
                                }
                                else if (!pt.IsValueType)
                                {
                                    genArgs[i] = null;
                                }
                                else
                                {
                                    genArgs[i] = Activator.CreateInstance(pt);
                                }
                            }

                            var result = genSync.Invoke(migrator, genArgs);
                            if (result is string s)
                            {
                                sql = s;
                            }
                            else if (result != null)
                            {
                                sql = result.ToString();
                            }
                        }
                        else
                        {
                            // Fallback to async variant via reflection
                            var genAsync = migratorType.GetMethod("GenerateScriptAsync", BindingFlags.Public | BindingFlags.Instance);
                            if (genAsync != null)
                            {
                                var parameters = genAsync.GetParameters();
                                object?[] asyncArgs = new object?[parameters.Length];
                                for (int i = 0; i < parameters.Length; i++)
                                {
                                    var pt = parameters[i].ParameterType;
                                    if (pt == typeof(bool))
                                    {
                                        asyncArgs[i] = true;
                                    }
                                    else if (pt.IsEnum)
                                    {
                                        var enumNames = Enum.GetNames(pt);
                                        string? match = Array.Find(enumNames, n => n.Contains("idempot", StringComparison.OrdinalIgnoreCase));
                                        if (match != null)
                                        {
                                            asyncArgs[i] = Enum.Parse(pt, match);
                                        }
                                        else if (parameters[i].HasDefaultValue)
                                        {
                                            asyncArgs[i] = parameters[i].DefaultValue;
                                        }
                                        else
                                        {
                                            asyncArgs[i] = Activator.CreateInstance(pt);
                                        }
                                    }
                                    else if (pt == typeof(CancellationToken))
                                    {
                                        asyncArgs[i] = CancellationToken.None;
                                    }
                                    else if (pt == typeof(string))
                                    {
                                        asyncArgs[i] = null;
                                    }
                                    else if (parameters[i].HasDefaultValue)
                                    {
                                        asyncArgs[i] = parameters[i].DefaultValue;
                                    }
                                    else if (!pt.IsValueType)
                                    {
                                        asyncArgs[i] = null;
                                    }
                                    else
                                    {
                                        asyncArgs[i] = Activator.CreateInstance(pt);
                                    }
                                }

                                var taskObj = genAsync.Invoke(migrator, asyncArgs);

                                if (taskObj is Task<string> ts)
                                {
                                    sql = await ts;
                                }
                                else if (taskObj is Task t)
                                {
                                    await t;
                                    var resultProp = taskObj.GetType().GetProperty("Result");
                                    if (resultProp != null)
                                    {
                                        sql = resultProp.GetValue(taskObj) as string;
                                    }
                                }
                                else if (taskObj != null)
                                {
                                    sql = taskObj.ToString();
                                }
                            }
                            else
                            {
                                throw new InvalidOperationException("No suitable GenerateScript method found on IMigrator implementation.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]✗[/] Failed to generate script for [cyan]{contextType.Name}[/]");
                        AnsiConsole.WriteLine(ex.Message);
                        hasErrors = true;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(sql))
                    {
                        scriptBuilder.AppendLine($"-- ============================================");
                        scriptBuilder.AppendLine($"-- DbContext: {contextType.Name}");
                        scriptBuilder.AppendLine($"-- Project: {projectPath}");
                        scriptBuilder.AppendLine($"-- ============================================");
                        scriptBuilder.AppendLine();
                        scriptBuilder.AppendLine(MakeSqlIdempotent(sql));
                        scriptBuilder.AppendLine();

                        AnsiConsole.MarkupLine($"[green]✓[/] Generated script for [cyan]{contextType.Name}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]⚠[/] No output generated for [cyan]{contextType.Name}[/]");
                        hasErrors = true;
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]✗[/] Failed to generate script for [cyan]{contextType.Name}[/]");
                    AnsiConsole.WriteLine(ex.Message);
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to generate script for [cyan]{contextType.Name}[/]");
                AnsiConsole.WriteLine(ex.Message);
                hasErrors = true;
            }
        }

        if (hasErrors)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Some scripts failed to generate[/]");
            return 1;
        }

        try
        {
            string? directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outputPath, scriptBuilder.ToString());
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]✓[/] [bold]Successfully wrote SQL script to:[/] [cyan]{outputPath}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Failed to write output file[/]");
            AnsiConsole.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Apply pending migrations for all or specified DbContexts (dev/test only)
    /// </summary>
    public async Task<int> ApplyMigrationsAsync(string[] args)
    {
        List<string>? contextFilter = null;

        // Parse arguments: [--contexts Ctx1,Ctx2]
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--contexts" && i + 1 < args.Length)
            {
                contextFilter = [.. args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            }
        }

        AnsiConsole.MarkupLine("[bold blue]Applying Pending Migrations[/]");
        AnsiConsole.MarkupLine("[yellow]⚠ This should only be used in development/test environments[/]");
        AnsiConsole.WriteLine();

        List<Type> contextTypes = GetAllDbContextTypes();
        if (contextFilter != null && contextFilter.Count > 0)
        {
            contextTypes = contextTypes.Where(t => contextFilter.Contains(t.Name)).ToList();
            AnsiConsole.MarkupLine($"[dim]Filtering to contexts:[/] {string.Join(", ", contextFilter)}");
            AnsiConsole.WriteLine();
        }

        if (contextTypes.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]✗[/] No DbContexts found matching filter");
            return 1;
        }

        int successCount = 0;
        int skippedCount = 0;
        int errorCount = 0;

        foreach (Type contextType in contextTypes)
        {
            DbContext? context = _serviceProvider.GetService(contextType) as DbContext;
            if (context == null)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠[/] Unable to resolve [cyan]{contextType.Name}[/]");
                errorCount++;
                continue;
            }

            try
            {
                IEnumerable<string> pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                if (!pendingMigrations.Any())
                {
                    AnsiConsole.MarkupLine($"[dim]○[/] [cyan]{contextType.Name}[/] is already up to date");
                    skippedCount++;
                    continue;
                }

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Applying {pendingMigrations.Count()} migration(s) for [cyan]{contextType.Name}[/]...", async ctx =>
                    {
                        // Use execution strategy to handle transient errors
                        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();
                        await strategy.ExecuteAsync(async () =>
                        {
                            await context.Database.MigrateAsync();
                        });
                    });

                AnsiConsole.MarkupLine($"[green]✓[/] Successfully applied migrations for [cyan]{contextType.Name}[/]");
                successCount++;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to apply migrations for [cyan]{contextType.Name}[/]");
                AnsiConsole.WriteLine(ex.Message);
                errorCount++;
            }
        }

        AnsiConsole.WriteLine();

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Status[/]")
            .AddColumn("[bold]Count[/]");

        summaryTable.AddRow("[green]Success[/]", $"[green]{successCount}[/]");
        summaryTable.AddRow("[dim]Skipped (up to date)[/]", $"[dim]{skippedCount}[/]");
        summaryTable.AddRow("[red]Errors[/]", errorCount > 0 ? $"[red]{errorCount}[/]" : $"[dim]{errorCount}[/]");

        AnsiConsole.Write(summaryTable);

        if (errorCount > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Some migrations failed to apply[/]");
            return 1;
        }

        if (successCount > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]✓[/] [bold]All migrations applied successfully[/]");
        }

        return 0;
    }

    // The shadow-Id refactor moved Guid PK generation from a SQL-side DEFAULT NewId() to a client-side
    // EF value generator (project_modular_shadow_id_generation_gap). The raw-SQL seed script inserts
    // rows without an Id, so a migrations-from-scratch database needs this default restored first
    // (mirrors TestcontainersTopologyFixture.RestoreSeedCompatibilityDefaultsAsync).
    private const string RestoreSeedCompatibilityDefaultsSql = """
        DECLARE @sql nvarchar(max) = N'';
        SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name)
            + N' ADD CONSTRAINT ' + QUOTENAME(N'DF_' + t.name + N'_Id_seedcompat')
            + N' DEFAULT NEWID() FOR ' + QUOTENAME(c.name) + N';'
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        WHERE ty.name = 'uniqueidentifier'
          AND c.name = 'Id'
          AND c.is_nullable = 0
          AND c.default_object_id = 0
          AND s.name = 'dbo';
        EXEC sp_executesql @sql;
        """;

    /// <summary>
    /// Run a seed SQL script against the UserOrgDBContext database (dev/test only): restores the
    /// NEWID() defaults the raw-SQL seed relies on, substitutes placeholders and executes every
    /// GO-delimited batch.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The batches are the operator-supplied seed script itself, run by a dev/test-only tool mode; there is no query composed from request input.")]
    public async Task<int> SeedAsync(string[] args)
    {
        SeedOptions options;
        try
        {
            options = SeedScript.Parse(args);
        }
        catch (ArgumentException ex)
        {
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Missing required argument[/]");
            AnsiConsole.MarkupLine($"[yellow]{ex.Message}[/]");
            AnsiConsole.MarkupLine("[yellow]Usage:[/] DatabaseMigrations seed --script <path> [--set NAME=VALUE]...");
            return 1;
        }

        AnsiConsole.MarkupLine("[bold blue]Seeding Database[/]");
        AnsiConsole.MarkupLine("[yellow]⚠ This should only be used in development/test environments[/]");
        AnsiConsole.WriteLine();

        DbContext? context = _serviceProvider.GetService(typeof(Core.DataAccess.Contexts.DatabaseContext)) as DbContext;
        string? connectionString = context?.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Missing connection string for UserOrgDBContext[/]");
            return 1;
        }

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            await using (var restoreCommand = connection.CreateCommand())
            {
                restoreCommand.CommandText = RestoreSeedCompatibilityDefaultsSql;
                await restoreCommand.ExecuteNonQueryAsync();
            }

            string script = await File.ReadAllTextAsync(options.ScriptPath);
            string substituted = SeedScript.Substitute(script, options.Values);
            IReadOnlyList<string> batches = SeedScript.SplitBatches(substituted);

            foreach (string batch in batches)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                await command.ExecuteNonQueryAsync();
            }

            AnsiConsole.MarkupLine($"[green]✓[/] [bold]Seed script executed:[/] {batches.Count} batch(es)");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]✗[/] [bold]Failed to run seed script[/]");
            AnsiConsole.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Get all registered DbContext types from the service provider
    /// </summary>
    private static string MakeSqlIdempotent(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return sql;

        var lines = Regex.Split(sql, "\r\n|\r|\n");
        var outLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            var tableMatch = Regex.Match(line, "^\\s*CREATE\\s+TABLE\\s+(\\[.*?\\](?:\\.\\[.*?\\])?)\\s*\\(", RegexOptions.IgnoreCase);
            if (tableMatch.Success)
            {
                var tableName = tableMatch.Groups[1].Value;
                var objectIdName = tableName.Contains('.', StringComparison.Ordinal) ? tableName : $"[dbo].{tableName}";

                // Avoid double-wrapping if already present
                bool alreadyWrapped = false;
                int prev = outLines.Count - 1;
                if (prev >= 0)
                {
                    int prevLinesToCheck = Math.Max(0, prev - 3);
                    for (int k = prev; k >= prevLinesToCheck; k--)
                    {
                        if (outLines[k].Contains("IF OBJECT_ID(", StringComparison.OrdinalIgnoreCase) && outLines[k].Contains(tableName, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyWrapped = true;
                            break;
                        }
                    }
                }

                if (!alreadyWrapped)
                {
                    outLines.Add($"IF OBJECT_ID(N'{objectIdName}') IS NULL");
                    outLines.Add("BEGIN");
                    outLines.Add(line);

                    // Copy until the closing ");"
                    i++;
                    for (; i < lines.Length; i++)
                    {
                        outLines.Add(lines[i]);
                        if (lines[i].TrimEnd().EndsWith(");", StringComparison.Ordinal))
                        {
                            break;
                        }
                    }

                    outLines.Add("END;");
                    continue;
                }
            }

            var idxMatch = Regex.Match(line, "^\\s*CREATE\\s+(UNIQUE\\s+)?INDEX\\s+(\\[.*?\\])\\s+ON\\s+(\\[.*?\\](?:\\.\\[.*?\\])?)", RegexOptions.IgnoreCase);
            if (idxMatch.Success)
            {
                var idxName = idxMatch.Groups[2].Value;
                var tableName = idxMatch.Groups[3].Value;
                var objectIdName = tableName.Contains('.', StringComparison.Ordinal) ? tableName : $"[dbo].{tableName}";

                outLines.Add($"IF NOT EXISTS (SELECT name FROM sys.indexes WHERE name = N'{idxName}' AND object_id = OBJECT_ID(N'{objectIdName}'))");
                outLines.Add("BEGIN");
                outLines.Add(line);
                outLines.Add("END;");
                continue;
            }

            outLines.Add(line);
        }

        return string.Join(Environment.NewLine, outLines);
    }

    private static List<Type> GetAllDbContextTypes()
    {
        // Explicit list of known DbContext types in this solution
        // Only includes contexts that are registered in DI container
        return
        [
            typeof(Core.DataAccess.Contexts.DatabaseContext),
            typeof(Forge.Core.Persistence.ForgeDbContext),
            typeof(Heappe.Persistence.HeappeIntegrationDbContext),
            typeof(Core.DataAccess.Outbox.OutboxDbContext),
            typeof(Core.DataAccess.ResourceRequests.ResourceRequestsDbContext),
            typeof(WorkflowEngine.Core.Persistence.WorkflowDbContext)
        ];
    }
}