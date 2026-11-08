using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Boots the real <c>Program</c> without external infrastructure under one <c>V1Api</c>/<c>V2Api</c>
/// combination, and keeps the service collection the host was built from so a test can inspect registrations
/// the built container no longer lists. <paramref name="configureServices"/> runs last, after every
/// registration of <c>Program</c>.
/// </summary>
internal sealed class ProgramBootFactory(
    bool isV1ApiEnabled,
    bool isV2ApiEnabled,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    static ProgramBootFactory()
    {
        ProgramBootEnvironment.Apply();
    }

    public IReadOnlyList<ServiceDescriptor> Registrations { get; private set; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"FeatureManagement:{FeatureFlags.V1Api}", isV1ApiEnabled ? "true" : "false");
        builder.UseSetting($"FeatureManagement:{FeatureFlags.V2Api}", isV2ApiEnabled ? "true" : "false");

        builder.ConfigureTestServices(services =>
        {
            ProgramBootEnvironment.RemoveHostedServices(services);

            configureServices?.Invoke(services);
            Registrations = [.. services];
        });
    }
}