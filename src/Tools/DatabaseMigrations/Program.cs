using Core.DataAccess.Outbox;
using Core.DataAccess.ResourceRequests;

using Heappe.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Spectre.Console;

using UserOrg.Tools.DatabaseMigrations;

var builder = Host.CreateApplicationBuilder(args);

string mode = args.Length > 0 ? args[0].ToUpperInvariant() : "HELP";

// Determine if we need a real connection string
// Script mode generates SQL in-process and does not require a real database connection
bool needsRealConnection = mode is not "SCRIPT" and not "HELP";

// Get connection strings (use dummy for script mode)
string userOrgConnectionString = needsRealConnection
    ? builder.Configuration.GetConnectionString("UserOrgDBContext") ?? throw new InvalidOperationException("ConnectionString 'UserOrgDBContext' not found.")
    : "Server=localhost;Database=Dummy;User Id=sa;Password=Dummy;TrustServerCertificate=true";

string forgeConnectionString = needsRealConnection
    ? builder.Configuration.GetConnectionString("ForgeDBContext") ?? throw new InvalidOperationException("ConnectionString 'ForgeDBContext' not found.")
    : "Server=localhost;Database=Dummy;User Id=sa;Password=Dummy;TrustServerCertificate=true";

string? heappeConnectionString = userOrgConnectionString;

// WorkflowEngine shares the UserOrgDBContext database, isolated by the 'workflow' EF schema (ADR-0056).
string workflowConnectionString = needsRealConnection
    ? builder.Configuration.GetConnectionString("WorkflowDBContext") ?? throw new InvalidOperationException("ConnectionString 'WorkflowDBContext' not found.")
    : "Server=localhost;Database=Dummy;User Id=sa;Password=Dummy;TrustServerCertificate=true";

// Core dbo schema — the modular Core.DataAccess context is the canonical migration owner (ADR-0049).
// Its domain dependencies are stubbed: they are never invoked during migration model-building, apply,
// or script generation (only the EF model is needed).
builder.Services.AddSingleton<Core.DataAccess.Events.DomainEventChannel>();
builder.Services.AddSingleton<Forge.Core.Features.Role.Services.IRoleService, MigrationStubRoleService>();
builder.Services.Configure<Core.Domain.User.WellKnownRoles>(_ => { });
builder.Services.AddDbContext<Core.DataAccess.Contexts.DatabaseContext>(options =>
    options.UseSqlServer(
        userOrgConnectionString,
        sqlOptions => sqlOptions.EnableRetryOnFailure()));

builder.Services.AddDbContext<Forge.Core.Persistence.ForgeDbContext>(options =>
    options.UseSqlServer(
        forgeConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                Forge.Core.Persistence.ForgeDbContext.SchemaName);
        }));

if (!string.IsNullOrWhiteSpace(heappeConnectionString))
{
    builder.Services.AddDbContext<HeappeIntegrationDbContext>(options =>
        options.UseSqlServer(
            heappeConnectionString,
            sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
                sqlOptions.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    HeappeIntegrationDbContext.SchemaName);
            }));
}

// Outbox persistence unit (dedicated 'outbox' schema, ADR-0041) — shares the UserOrgDBContext database
builder.Services.AddDbContext<OutboxDbContext>(options =>
    options.UseSqlServer(
        userOrgConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                OutboxDbContext.SchemaName);
        }));

// Resource-request projection unit (dedicated 'resourceRequests' schema, ADR-0063) — shares the UserOrgDBContext database
builder.Services.AddDbContext<ResourceRequestsDbContext>(options =>
    options.UseSqlServer(
        userOrgConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                ResourceRequestsDbContext.SchemaName);
        }));

// WorkflowEngine persistence unit (dedicated 'workflow' schema, ADR-0056) — shares the UserOrgDBContext database
builder.Services.AddDbContext<WorkflowEngine.Core.Persistence.WorkflowDbContext>(options =>
    options.UseSqlServer(
        workflowConnectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable(
                "__EFMigrationsHistory",
                WorkflowEngine.Core.Persistence.WorkflowDbContext.SchemaName);
        }));

builder.Services.AddScoped<MigrationRunner>();

// Disable logging to reduce noise
builder.Logging.ClearProviders();

using var host = builder.Build();

int exitCode = 0;

try
{
    using var scope = host.Services.CreateScope();
    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();

    exitCode = mode switch
    {
        "LIST" => await runner.ListMigrationsAsync(),
        "CHECK" => await runner.CheckMigrationsAsync(),
        "SCRIPT" => await runner.GenerateScriptAsync(args),
        "APPLY" => await runner.ApplyMigrationsAsync(args),
        "SEED" => await runner.SeedAsync(args),
        _ => ShowHelp()
    };
}
catch (InvalidOperationException ex) when (ex.Message.Contains("ConnectionString", StringComparison.Ordinal))
{
    AnsiConsole.MarkupLine("[red]✗[/] [bold]Configuration Error[/]");
    AnsiConsole.MarkupLine("[yellow]Missing required connection string[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[dim]Required environment variables:[/]");
    AnsiConsole.MarkupLine("  [cyan]ConnectionStrings__UserOrgDBContext[/]");
    AnsiConsole.MarkupLine("  [cyan]ConnectionStrings__ForgeDBContext[/]");
    exitCode = 1;
}
#pragma warning disable CA1031
catch (Exception ex)
{
    AnsiConsole.MarkupLine("[red]✗[/] [bold]Unexpected Error[/]");
    AnsiConsole.WriteLine(ex.Message);
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine($"[dim]Error type:[/] {ex.GetType().Name}");
    exitCode = 1;
}
#pragma warning restore CA1031

await host.StopAsync();

Environment.Exit(exitCode);

static int ShowHelp()
{
    AnsiConsole.Write(
        new FigletText("UserOrg Migrations")
            .LeftJustified()
            .Color(Color.Blue));

    AnsiConsole.WriteLine();

    AnsiConsole.MarkupLine("[bold]Database Migration Tool for UserOrg Aspire Project[/]");
    AnsiConsole.WriteLine();

    var table = new Table()
        .Border(TableBorder.Rounded)
        .AddColumn("[bold]Mode[/]")
        .AddColumn("[bold]Description[/]")
        .AddColumn("[bold]Example[/]");

    table.AddRow(
        "[cyan]list[/]",
        "List all DbContexts and migration status",
        "[dim]DatabaseMigrations list[/]");

    table.AddRow(
        "[cyan]check[/]",
        "Check if all migrations are applied",
        "[dim]DatabaseMigrations check[/]");

    table.AddRow(
        "[cyan]script[/]",
        "Generate idempotent SQL script [dim](no connection needed)[/]",
        "[dim]DatabaseMigrations script --output ./migration.sql[/]");

    table.AddRow(
        "[cyan]apply[/]",
        "Apply pending migrations [yellow](dev/test only)[/]",
        "[dim]DatabaseMigrations apply[/]");

    table.AddRow(
        "[cyan]seed[/]",
        "Run a seed SQL script with placeholder substitution [yellow](dev/test only)[/]",
        "[dim]DatabaseMigrations seed --script seed-data.sql --set NAME=VALUE[/]");

    table.AddRow(
        "[cyan]help[/]",
        "Show this help message",
        "[dim]DatabaseMigrations help[/]");

    AnsiConsole.Write(table);

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[bold]Options:[/]");
    AnsiConsole.MarkupLine("  [cyan]--output <path>[/]       Output file path for script mode");
    AnsiConsole.MarkupLine("  [cyan]--contexts <list>[/]     Comma-separated list of DbContext names to process");
    AnsiConsole.MarkupLine("  [cyan]--script <path>[/]       Seed SQL script path for seed mode");
    AnsiConsole.MarkupLine("  [cyan]--set NAME=VALUE[/]      Placeholder to substitute in the seed script (repeatable)");
    AnsiConsole.WriteLine();

    AnsiConsole.MarkupLine("[bold]Environment Variables:[/]");
    AnsiConsole.MarkupLine("  [cyan]ConnectionStrings__UserOrgDBContext[/]   Connection string for DatabaseContext [dim](not needed for script mode)[/]");
    AnsiConsole.MarkupLine("  [cyan]ConnectionStrings__ForgeDBContext[/]     Connection string for ForgeDbContext [dim](not needed for script mode)[/]");
    AnsiConsole.WriteLine();

    AnsiConsole.MarkupLine("[bold]Exit Codes:[/]");
    AnsiConsole.MarkupLine("  [green]0[/] = Success");
    AnsiConsole.MarkupLine("  [red]1[/] = Error or pending migrations (check mode)");

    return 0;
}
