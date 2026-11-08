using Forge.Core.Features.EndpointMap.Persistence;
using Forge.Core.Features.Permission.Persistence;
using Forge.Core.Features.Role.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Forge.Core.Persistence;

internal class ForgeDbContext : DbContext
{
    internal const string SchemaName = "forge";

    internal DbSet<Role> Roles { get; set; } = null!;
    internal DbSet<Permission> Permissions { get; set; } = null!;
    internal DbSet<EndpointMapping> EndpointMappings { get; set; } = null!;

    public ForgeDbContext(DbContextOptions<ForgeDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ForgeDbContext).Assembly);
    }
}

internal sealed class ForgeDbContextFactory : IDesignTimeDbContextFactory<ForgeDbContext>
{
    public ForgeDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ForgeDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ForgeDBContext");
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", ForgeDbContext.SchemaName);
        });
        return new ForgeDbContext(optionsBuilder.Options);
    }
}