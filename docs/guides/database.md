# Database Guide

Database setup, migrations, backup, and restore procedures.

## Overview

UserOrg uses SQL Server for persistence with Entity Framework Core migrations for schema management.

## Local Development Setup

### Prerequisites

- SQL Server running (via Aspire AppHost)
- .NET SDK installed
- Visual Studio or VS Code

### Running Migrations

Each persistence unit owns a `*MigrationStartupService` that calls `MigrateAsync` on boot, gated by
`Database:AutoMigrateOnStartup` (default `false`). With it off, apply migrations explicitly — see
[Entity Framework Migrations](#entity-framework-migrations) below.

## Backup & Restore

### Backing Up the Database

#### Via Aspire Dashboard

1. Open Aspire Dashboard (usually http://localhost:17010)
2. Find the `userorgDatabase` resource
3. Click the resource and look for backup action
4. Download the backup file

#### Via SQL Server Management Studio

```sql
BACKUP DATABASE [UserOrgDBContext] 
TO DISK = 'C:\backups\userorg_backup.bak'
WITH INIT, COMPRESSION;
```

#### Via Command Line

```bash
sqlcmd -S localhost -U sa -P <password> `
  -Q "BACKUP DATABASE [UserOrgDBContext] TO DISK = 'C:\backups\userorg_backup.bak' WITH INIT, COMPRESSION;"
```

### Restoring from Backup

#### Step 1: Place Backup File

Place your `.bak` backup file in the `src/UserOrg.AppHost/dbbackup` folder.

Example: `src/UserOrg.AppHost/dbbackup/userorg_230126_1324.bak`

#### Step 2: Update AppHost Configuration

Update the AppHost configuration to reference your backup:

```csharp
// src/UserOrg.AppHost/Program.cs
var sql = builder.AddSqlServer("userorgDatabase", password: dbPassword)
    .WithHostPort(1433)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDatabaseRestoreCommand("userorg_230126_1324.bak", "UserOrgDBContext")
    .WithDbGate();
```

Update the filename in `WithDatabaseRestoreCommand` to match your backup file.

#### Step 3: Start the Application

```bash
cd src/UserOrg.AppHost
dotnet run
```

The database will be restored automatically on startup.

#### Step 4: Sync Aspire Dashboard

After restore completes:

1. Open Aspire Dashboard
2. Find the `userorgDatabase` resource
3. Click "Restore" action
4. Wait for sync to complete

This ensures the dashboard metadata matches the restored database.

### Troubleshooting Restore

**Issue:** "Restore failed — file not found"
- Verify file path is correct
- Check file permissions
- Ensure file is in the expected directory

**Issue:** "Database in use"
- Stop the application
- Wait for connections to close (30 seconds)
- Retry restore

**Issue:** "Aspire dashboard out of sync"
- Run the "Restore" action in dashboard again
- Check resource status
- Verify database connection

## Database Maintenance

### Checking Database Size

```sql
SELECT 
    DB_NAME() as DatabaseName,
    CAST(SUM(size) * 8. / 1024 / 1024 AS DECIMAL(8,2)) as SizeMB
FROM sys.master_files
WHERE DB_NAME(database_id) = 'UserOrgDBContext'
GROUP BY database_id;
```

### Rebuilding Indexes

```sql
USE UserOrgDBContext;
DECLARE @TableName sysname;

DECLARE table_cursor CURSOR FOR
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE';

OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @TableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    ALTER INDEX ALL ON @TableName REBUILD;
    FETCH NEXT FROM table_cursor INTO @TableName;
END;

CLOSE table_cursor;
DEALLOCATE table_cursor;
```

### Updating Statistics

```sql
USE UserOrgDBContext;
EXEC sp_MSForEachTable 'UPDATE STATISTICS ? WITH ALL'
```

## Entity Framework Migrations

### The six persistence units

`dbo` migrations are owned by the **modular** `Core.DataAccess` context (ADR-0049); the legacy
`src/Core/Infrastructure.DataAccess` context still runs at runtime but no longer owns migrations.

| DbContext | Schema | `-p` (migrations project) |
|---|---|---|
| `Core.DataAccess.Contexts.DatabaseContext` | `dbo` | `src/Modules/Core.DataAccess/Core.DataAccess.csproj` |
| `Core.DataAccess.Outbox.OutboxDbContext` | `outbox` | `src/Modules/Core.DataAccess/Core.DataAccess.csproj` |
| `Core.DataAccess.ResourceRequests.ResourceRequestsDbContext` | `resourcerequests` | `src/Modules/Core.DataAccess/Core.DataAccess.csproj` |
| `Forge.Core.Persistence.ForgeDbContext` | `forge` | `src/Modules/Libraries/Forge.Core/Forge.Core.csproj` |
| `Heappe.Persistence.HeappeIntegrationDbContext` | `heappe` | `src/Modules/Integrations/Heappe/Heappe.csproj` |
| `WorkflowEngine.Core.Persistence.WorkflowDbContext` | `workflow` | `src/Modules/Libraries/WorkflowEngine.Core/WorkflowEngine.Core.csproj` |

Adding a context means adding it to **both** hardcoded lists in
`src/Tools/DatabaseMigrations/MigrationRunner.cs` (`GetAllDbContextTypes()` and the
`GenerateScriptAsync` `contextProjects` map) — miss one and CI silently ships an incomplete
`migration.sql`.

### Picking the startup project (`-s`)

`dotnet ef` needs a *startup* project to resolve the context.

**The three `Core.DataAccess` contexts** ship `IDesignTimeDbContextFactory` implementations, so EF never
has to boot a host. Use the API as startup project; the Vault warning it prints is expected and harmless
— the factory wins.

```bash
dotnet ef migrations list -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

**Forge / Heappe / WorkflowEngine** have no design-time factory. Use the migrations tool as startup
project and hand it throwaway connection strings, since its host demands them:

```bash
ConnectionStrings__UserOrgDBContext="Server=localhost;Database=Dummy;User Id=sa;Password=Dummy;TrustServerCertificate=true" ConnectionStrings__ForgeDBContext="$ConnectionStrings__UserOrgDBContext" ConnectionStrings__WorkflowDBContext="$ConnectionStrings__UserOrgDBContext" dotnet ef migrations list -s src/Tools/DatabaseMigrations/DatabaseMigrations.csproj -p src/Modules/Libraries/Forge.Core/Forge.Core.csproj -c Forge.Core.Persistence.ForgeDbContext --no-connect
```

Add `--no-build` once you have built, and `--no-connect` to any `list` / `script` that should not touch
a database.

### Creating a migration

```bash
dotnet ef migrations add AddSomething -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext -o Migrations
```

`-o` is relative to `-p`: `Migrations` for `dbo`, `Outbox/Migrations` and `ResourceRequests/Migrations`
for the other two, `Persistence/Migrations` for Forge / Heappe / WorkflowEngine.

### The zero-diff probe (ADR-0049 gate)

Before changing entity configuration, prove the model still matches the schema — scaffold a throwaway
migration, check that `Up()` / `Down()` come out empty, then delete it:

```bash
dotnet ef migrations add _Probe -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext -o Migrations
```

```bash
dotnet ef migrations remove -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

A non-empty probe means the configuration diverged from the real schema. Fix the configuration — never
ship the scaffolded DDL against a live schema.

### Listing and inspecting

```bash
dotnet ef migrations list -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext --no-connect
```

```bash
dotnet ef dbcontext info -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

### Generating SQL

Review what a migration actually emits — always do this for hand-authored or destructive migrations:

```bash
dotnet ef migrations script <FromMigrationId> <ToMigrationId> -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext -o check.sql
```

Omit both ids for the full chain from zero; use `--idempotent` to wrap each migration in an
`IF NOT EXISTS(... __EFMigrationsHistory ...)` guard.

That per-migration guard does **not** make the statements *inside* a migration re-runnable. EF renders
`AlterColumn(oldDefaultValueSql: ...)` as a `sys.default_constraints` lookup behind `IF @var IS NOT NULL`,
so those are safe; a plain `DropColumn` / `DropIndex` / `DropForeignKey` is not. When a removal may
already have happened on some databases, write it as guarded `migrationBuilder.Sql` — see
`20260630191024_ModularCleanup` and ADR-0100.

### Applying migrations

```bash
dotnet ef database update -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

Target a specific migration to move forwards or roll back; `0` reverts everything:

```bash
dotnet ef database update 20260721115528_DropProjectResourceAvailability -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

### Removing an unapplied migration

Drops the most recent migration file and rewinds the model snapshot. Only safe while the migration is
still unapplied *everywhere* — once it is in any `__EFMigrationsHistory`, write a new migration instead.

```bash
dotnet ef migrations remove -s src/Core/API/API.csproj -p src/Modules/Core.DataAccess/Core.DataAccess.csproj -c Core.DataAccess.Contexts.DatabaseContext
```

### All contexts at once — the `DatabaseMigrations` tool

`dotnet ef` drives one context per invocation. The repo's tool walks all six and is what CI uses:

```bash
dotnet run --project src/Tools/DatabaseMigrations -- list
```

| Command | Effect |
|---|---|
| `list` | Per-context totals plus the pending migration names |
| `check` | Exit code 1 if any context has pending migrations |
| `script --output migrations/sql/migration.sql` | Combined SQL for all contexts; no database needed |
| `apply` | Runs `MigrateAsync` on every context |

`list`, `check` and `apply` need real `ConnectionStrings__*` values; `script` does not. CI generates
`migrations/sql/migration.sql` in the `migration_sql` job via `.gitlab/scripts/generate-migration-sql.sh`
and publishes it as a release artifact — it is not committed.

### Migration structure

```csharp
public partial class AddNewFeature : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("NewTable", ...);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("NewTable");
    }
}
```

## Connection Strings

### Development (Local)

```
Server=localhost;Database=UserOrgDBContext;User Id=sa;Password=<password>;Encrypt=False;
```

### Aspire AppHost

Database is created automatically and managed by Aspire.

Connection string is injected via `AddSqlServer("userorgDatabase")`.

## Performance Tips

1. **Index Strategy**
   - Add indexes on frequently queried columns
   - Review slow query logs
   - Update index statistics regularly

2. **Query Optimization**
   - Use `Include()` for related data
   - Use `AsNoTracking()` for read-only queries
   - Batch operations when possible

3. **Connection Pooling**
   - Enabled by default in connection string
   - Pool size: 15-100 connections
   - Adjust based on workload

## Status

✅ Fully implemented and production-ready

- Automated migrations on startup
- Backup and restore procedures
- Entity Framework configuration
- Performance optimized indexes
