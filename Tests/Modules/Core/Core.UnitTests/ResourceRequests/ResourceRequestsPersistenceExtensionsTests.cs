using Core.DataAccess.ResourceRequests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Verifies that SQL command-logging events are suppressed outside Development on
/// <see cref="ResourceRequestsPersistenceExtensions.AddResourceRequestsPersistence"/>: ensures
/// <see cref="RelationalEventId.CommandExecuting"/> and <see cref="RelationalEventId.CommandExecuted"/> are ignored
/// in production, left enabled in Development, and <see cref="RelationalEventId.CommandError"/> is never ignored.
/// </summary>
public sealed class ResourceRequestsPersistenceExtensionsTests
{
    private static HostApplicationBuilder CreateBuilder(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration["ConnectionStrings:UserOrgDBContext"] =
            "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";
        return builder;
    }

    private static WarningsConfiguration ResolveWarnings(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<ResourceRequestsDbContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!.WarningsConfiguration;
    }

    [Fact(DisplayName = "AddResourceRequestsPersistence suppresses SQL command events outside Development")]
    public void AddResourceRequestsPersistence_OutsideDevelopment_SuppressesCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddResourceRequestsPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddResourceRequestsPersistence leaves SQL command events enabled in Development")]
    public void AddResourceRequestsPersistence_InDevelopment_LeavesCommandEventsEnabled()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.AddResourceRequestsPersistence();

        var warnings = ResolveWarnings(builder);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }
}
