using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Persistence;

namespace WorkflowEngine.Core.Tests.DependencyInjection;

/// <summary>
/// Verifies that SQL command-logging events are suppressed outside Development on <see cref="ServiceCollectionExtensions.AddWorkflowEngine"/>.
/// Suppression is inlined within the module (rather than using a shared helper) because <c>WorkflowEngine.Core</c> is domain-agnostic
/// and must not depend on <c>UserOrg.Shared</c>. Ensures <see cref="RelationalEventId.CommandExecuting"/> and <see cref="RelationalEventId.CommandExecuted"/>
/// are ignored in production, left enabled in Development, and <see cref="RelationalEventId.CommandError"/> is never ignored.
/// </summary>
public sealed class ServiceCollectionExtensionsSqlLoggingTests
{
    private static HostApplicationBuilder CreateBuilder(string environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment });
        builder.Configuration["ConnectionStrings:WorkflowDBContext"] =
            "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";
        return builder;
    }

    private static WarningsConfiguration ResolveWarnings(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<WorkflowDbContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!.WarningsConfiguration;
    }

    [Fact(DisplayName = "AddWorkflowEngine suppresses SQL command events outside Development")]
    public void AddWorkflowEngine_OutsideDevelopment_SuppressesCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddWorkflowEngine();

        var warnings = ResolveWarnings(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddWorkflowEngine leaves SQL command events enabled in Development")]
    public void AddWorkflowEngine_InDevelopment_LeavesCommandEventsEnabled()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.AddWorkflowEngine();

        var warnings = ResolveWarnings(builder);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }
}
