using System;
using System.Linq;
using System.Threading.Tasks;

using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Heappe.UnitTests.Persistence;

/// <summary>
/// Pins the B05 fix for #280 on <see cref="HeappeIntegrationDbContext"/>: each of the six
/// Guid-PK entities mints its own v7 id at construction and is configured
/// <c>ValueGeneratedNever</c>, so two inserts never collide on an all-zeros PK. The full model
/// sweep lives in B06's convention test; these pin the mint point per entity.
/// </summary>
public sealed class EntityIdMintingTests
{
    [Fact(DisplayName = "CommandTemplateDefinition mints a v7 id on construction")]
    public void Should_mint_id_for_CommandTemplateDefinition()
    {
        AssertMintsVersion7Id(new CommandTemplateDefinition());
    }

    [Fact(DisplayName = "CommandTemplateLocationDefinition mints a v7 id on construction")]
    public void Should_mint_id_for_CommandTemplateLocationDefinition()
    {
        AssertMintsVersion7Id(new CommandTemplateLocationDefinition());
    }

    [Fact(DisplayName = "CommandTemplateParameterDefinition mints a v7 id on construction")]
    public void Should_mint_id_for_CommandTemplateParameterDefinition()
    {
        AssertMintsVersion7Id(new CommandTemplateParameterDefinition());
    }

    [Fact(DisplayName = "LocationPartition mints a v7 id on construction")]
    public void Should_mint_id_for_LocationPartition()
    {
        AssertMintsVersion7Id(new LocationPartition());
    }

    [Fact(DisplayName = "ProjectCommandTemplatePartition mints a v7 id on construction")]
    public void Should_mint_id_for_ProjectCommandTemplatePartition()
    {
        AssertMintsVersion7Id(new ProjectCommandTemplatePartition());
    }

    [Fact(DisplayName = "ProjectCommandTemplate mints a v7 id on construction")]
    public void Should_mint_id_for_ProjectCommandTemplate()
    {
        AssertMintsVersion7Id(new ProjectCommandTemplate());
    }

    [Fact(DisplayName = "Two directly constructed ProjectCommandTemplate rows insert without a PK collision")]
    public async Task Should_insert_two_ProjectCommandTemplate_rows_without_collision()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);

        var first = new ProjectCommandTemplate();
        var second = new ProjectCommandTemplate();
        dbContext.ProjectCommandTemplates.AddRange(first, second);

        // Name has a private setter (the factories own it); the required column is filled through the
        // change tracker so the insert fails on the PK invariant under test, not on a NOT NULL check.
        dbContext.Entry(first).Property(x => x.Name).CurrentValue = "template-a";
        dbContext.Entry(second).Property(x => x.Name).CurrentValue = "template-b";

        await dbContext.SaveChangesAsync();

        var storedIds = await dbContext.ProjectCommandTemplates
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, storedIds.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, storedIds);
        Assert.Contains(first.Id, storedIds);
        Assert.Contains(second.Id, storedIds);
    }

    [Fact(DisplayName = "Two client-minted LocationPartition rows insert without a PK collision")]
    public async Task Should_insert_two_LocationPartition_rows_without_collision()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);

        // CreatedAt is set explicitly because AuditableEntity's column default (SYSUTCDATETIME())
        // is SQL-Server-only and SQLite does not understand it; unrelated to the PK invariant under test.
        var first = new LocationPartition { Name = "partition-a", LocationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        var second = new LocationPartition { Name = "partition-b", LocationId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
        dbContext.LocationPartitions.AddRange(first, second);

        await dbContext.SaveChangesAsync();

        var storedIds = await dbContext.LocationPartitions
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, storedIds.Distinct().Count());
        Assert.DoesNotContain(Guid.Empty, storedIds);
        Assert.Contains(first.Id, storedIds);
        Assert.Contains(second.Id, storedIds);
    }

    [Fact(DisplayName = "ProjectCommandTemplate keeps its factory-minted id after ValueGeneratedNever")]
    public async Task Should_persist_ProjectCommandTemplate_factory_minted_id()
    {
        using var connection = CreateOpenSqliteConnection();
        using var dbContext = CreateSqliteDbContext(connection);

        var template = ProjectCommandTemplate.Create("template", Heappe.Persistence.Enums.ECommandTemplateType.Unknown, Guid.NewGuid(), Guid.NewGuid());
        var mintedId = template.Id;
        dbContext.ProjectCommandTemplates.Add(template);

        await dbContext.SaveChangesAsync();

        var storedId = await dbContext.ProjectCommandTemplates.AsNoTracking().Select(x => x.Id).SingleAsync();
        Assert.Equal(mintedId, storedId);
        Assert.NotEqual(Guid.Empty, storedId);
    }

    private static void AssertMintsVersion7Id<TEntity>(TEntity entity) where TEntity : notnull
    {
        var id = (Guid)typeof(TEntity).GetProperty("Id")!.GetValue(entity)!;

        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(7, id.ToByteArray()[7] >> 4);
    }

    private static SqliteConnection CreateOpenSqliteConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static HeappeIntegrationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new HeappeIntegrationDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
