using Core.Domain.User;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using UserOrganizationManagement.API.Composition;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Guards the ALWAYS/FLAGGED split of <c>AddV2Core</c>. With <c>FeatureManagement:V2Api</c> off the
/// host must still compose everything v1 depends on (dbo migrations, Forge, the output-cache
/// middleware, the <see cref="WellKnownRoles"/> binding) while none of the v2-only persistence
/// units and background workers are registered.
/// It stands up the real host composition — no infrastructure is contacted, the container is only
/// built and inspected — so it is tagged as an integration test per the repository convention.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class V2CompositionGateTests
{
    private static readonly string[] _v2OnlyHostedServices =
    [
        "OutboxDispatcherService",
        "OutboxMigrationStartupService",
        "ResourceRequestsMigrationStartupService",
        "WorkflowMigrationStartupService",
        "RequestWorker",
        "RequestWorkerStartupRecovery",
        "RetryScheduledScanner",
    ];

    private static readonly string[] _alwaysHostedServices =
    [
        "CoreDatabaseMigrationStartupService",
        "ForgeMigrationStartupService",
    ];

    [Fact]
    public void Should_NotRegisterV2OnlyHostedServices_When_V2ApiDisabled()
    {
        var builder = ComposeHost(isV2ApiEnabled: false);

        Assert.Empty(HostedServiceNames(builder).Intersect(_v2OnlyHostedServices));
    }

    [Fact]
    public void Should_RegisterV2OnlyHostedServices_When_V2ApiEnabled()
    {
        var builder = ComposeHost(isV2ApiEnabled: true);

        var registered = HostedServiceNames(builder).Intersect(_v2OnlyHostedServices);

        Assert.Equal(_v2OnlyHostedServices.Order(), registered.Order());
    }

    [Fact]
    public void Should_RegisterMigrationHostedServices_When_V2ApiDisabled()
    {
        var builder = ComposeHost(isV2ApiEnabled: false);

        var registered = HostedServiceNames(builder).Intersect(_alwaysHostedServices);

        Assert.Equal(_alwaysHostedServices.Order(), registered.Order());
    }

    [Fact]
    public void Should_ResolveOutputCacheStore_When_V2ApiDisabled()
    {
        // app.UseOutputCache() is unconditional in Program.cs; the named policies live in the flagged
        // AddCore, so the host itself must supply the base registration.
        using var app = ComposeHost(isV2ApiEnabled: false).Build();

        Assert.NotNull(app.Services.GetService<IOutputCacheStore>());
    }

    [Fact]
    public void Should_ResolveConfiguredWellKnownRoles_When_V2ApiDisabled()
    {
        // DatabaseContext resolves these for v1 role lookups and IOptions<T> degrades silently to
        // type defaults, so an unbound binding would corrupt role lookups with no startup failure.
        using var app = ComposeHost(isV2ApiEnabled: false).Build();

        Assert.Equal("SuperAdmin", app.Services.GetRequiredService<IOptions<WellKnownRoles>>().Value.DefaultAdminRoleName);
    }

    private static WebApplicationBuilder ComposeHost(bool isV2ApiEnabled)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Dummy values only — the test never opens a connection, contacts Vault, or starts the host.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AuthenticationConfiguration:MetadataAddress"] = "https://composition-test.invalid",
            ["MailConfiguration:MailSenderUrl"] = "https://composition-test.invalid",
            ["ConnectionStrings:UserOrgDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["ConnectionStrings:ForgeDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["ConnectionStrings:WorkflowDBContext"] = "Server=(local);Database=CompositionTest;Trusted_Connection=True;",
            ["DefaultAdminRoleName"] = "SuperAdmin",
        });

        builder.AddHostInfrastructure();
        builder.AddV2Core(isV2ApiEnabled);

        return builder;
    }

    private static IEnumerable<string> HostedServiceNames(WebApplicationBuilder builder) =>
        builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType?.Name)
            .OfType<string>();
}
