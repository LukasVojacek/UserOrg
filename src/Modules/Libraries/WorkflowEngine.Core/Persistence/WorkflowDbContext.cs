using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

using WorkflowEngine.Core.Domain.Request;

namespace WorkflowEngine.Core.Persistence;

internal sealed class WorkflowDbContext : DbContext
{
    internal const string SchemaName = "workflow";

    internal DbSet<RequestAggregate> Requests { get; set; } = null!;
    internal DbSet<RequestStep> RequestSteps { get; set; } = null!;

    public WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorkflowDbContext).Assembly);
    }
}

internal sealed class WorkflowDbContextFactory : IDesignTimeDbContextFactory<WorkflowDbContext>
{
    public WorkflowDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WorkflowDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__WorkflowDBContext");
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
            sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", WorkflowDbContext.SchemaName);
        });

        return new WorkflowDbContext(optionsBuilder.Options);
    }
}