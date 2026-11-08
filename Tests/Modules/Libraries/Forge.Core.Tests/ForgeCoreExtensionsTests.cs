using Forge.Core.Features.UserOrgRoles;
using Forge.Core.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Forge.Core.Tests;

public sealed class ForgeCoreExtensionTests
{
    [Fact(DisplayName = "AddForgeCore throws when builder is null")]
    public void AddForgeCore_NullBuilder_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ForgeCoreExtensions.AddForgeCore(null!));
    }

    private static WebApplicationBuilder CreateBuilder() => CreateBuilder(Environments.Production);

    private static WebApplicationBuilder CreateBuilder(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration["ConnectionStrings:ForgeDBContext"] =
            "Server=(local);Database=fake;Trusted_Connection=True;TrustServerCertificate=True;";
        return builder;
    }

    private static WarningsConfiguration ResolveWarnings<TContext>(WebApplicationBuilder builder)
        where TContext : DbContext
    {
        using var provider = builder.Services.BuildServiceProvider();
        var options = provider.GetRequiredService<DbContextOptions<TContext>>();
        var core = options.FindExtension<CoreOptionsExtension>();
        Assert.NotNull(core);
        return core!.WarningsConfiguration;
    }

    [Fact(DisplayName = "AddForgeCore registers pooled ForgeDbContext factory as singleton")]
    public void AddForgeCore_RegistersForgeDbContextFactory_AsSingleton()
    {
        var builder = CreateBuilder();

        builder.AddForgeCore();

        var descriptor = builder.Services.SingleOrDefault(d => d.ServiceType == typeof(IDbContextFactory<ForgeDbContext>));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact(DisplayName = "AddForgeCore registers pooled UserOrgDbContext factory as singleton")]
    public void AddForgeCore_RegistersUserOrgDbContextFactory_AsSingleton()
    {
        var builder = CreateBuilder();

        builder.AddForgeCore();

        var descriptor = builder.Services.SingleOrDefault(d => d.ServiceType == typeof(IDbContextFactory<UserOrgDbContext>));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    [Fact(DisplayName = "AddForgeCore registers ForgeMigrationStartupService and UserOrgMigrationStartupService as hosted services")]
    public void AddForgeCore_RegistersMigrationStartupServices_AsHostedServices()
    {
        var builder = CreateBuilder();

        builder.AddForgeCore();

        var hostedServiceImplementations = builder.Services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(ForgeMigrationStartupService), hostedServiceImplementations);
        Assert.Contains(typeof(UserOrgMigrationStartupService), hostedServiceImplementations);
    }

    [Fact(DisplayName = "AddForgeRoleQueries registers only ForgeDbContext factory, not UserOrgDbContext factory")]
    public void AddForgeRoleQueries_RegistersOnlyForgeDbContextFactory()
    {
        var builder = CreateBuilder();

        builder.AddForgeRoleQueries();

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IDbContextFactory<ForgeDbContext>));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IDbContextFactory<UserOrgDbContext>));
    }

    [Fact(DisplayName = "AddForgeRoleQueries registers ForgeMigrationStartupService as a hosted service")]
    public void AddForgeRoleQueries_RegistersForgeMigrationStartupService()
    {
        var builder = CreateBuilder();

        builder.AddForgeRoleQueries();

        var hostedServiceImplementations = builder.Services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .ToList();

        Assert.Contains(typeof(ForgeMigrationStartupService), hostedServiceImplementations);
        Assert.DoesNotContain(typeof(UserOrgMigrationStartupService), hostedServiceImplementations);
    }

    [Fact(DisplayName = "AddForgeCore suppresses SQL command events on ForgeDbContext outside Development")]
    public void AddForgeCore_OutsideDevelopment_SuppressesForgeDbContextCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddForgeCore();

        var warnings = ResolveWarnings<ForgeDbContext>(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuting));
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandError));
    }

    [Fact(DisplayName = "AddForgeCore suppresses SQL command events on UserOrgDbContext outside Development")]
    public void AddForgeCore_OutsideDevelopment_SuppressesUserOrgDbContextCommandEvents()
    {
        var builder = CreateBuilder(Environments.Production);

        builder.AddForgeCore();

        var warnings = ResolveWarnings<UserOrgDbContext>(builder);
        Assert.Equal(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }

    [Fact(DisplayName = "AddForgeCore leaves SQL command events enabled in Development")]
    public void AddForgeCore_InDevelopment_LeavesForgeDbContextCommandEventsEnabled()
    {
        var builder = CreateBuilder(Environments.Development);

        builder.AddForgeCore();

        var warnings = ResolveWarnings<ForgeDbContext>(builder);
        Assert.NotEqual(WarningBehavior.Ignore, warnings.GetBehavior(RelationalEventId.CommandExecuted));
    }
}