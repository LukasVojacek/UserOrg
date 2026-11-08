using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using Xunit;

namespace Heappe.UnitTests;

public sealed class HeappeResilienceOptionsTests
{
    // Regression guard for the boot-time crash: disabling retries via MaxRetryAttempts = 0 failed the standard
    // resilience options validator (the field must be >= 1), throwing OptionsValidationException at startup —
    // a failure the build never catches. Resolving the named options here runs the same validators that
    // ValidateOnStart runs at boot, so it fails loudly if ConfigureDataResilience ever produces invalid options.
    [Fact]
    public void Data_client_resilience_options_pass_startup_validation()
    {
        var services = new ServiceCollection();
        services.AddHttpClient(HeappeIntegrationExtensions.HeappeClientName)
            .AddStandardResilienceHandler(o => HeappeIntegrationExtensions.ConfigureDataResilience(o, new HeappeHttpClientOptions()));

        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>();

        var exception = Record.Exception(() => monitor.Get($"{HeappeIntegrationExtensions.HeappeClientName}-standard"));

        Assert.Null(exception);
    }
}
