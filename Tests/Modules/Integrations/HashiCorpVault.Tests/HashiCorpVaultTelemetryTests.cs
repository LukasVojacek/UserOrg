using System.Diagnostics;

using HashiCorpVault.Contracts;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace HashiCorpVault.Tests;

public sealed class HashiCorpVaultTelemetryTests
{
    [Fact]
    public async Task VaultClient_EmitsActivity_WhenTracerRegistered()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("Vault").ConfigureHttpClient(c => c.BaseAddress = new Uri("http://localhost/"));
        services.AddTransient<IHashiCorpVaultClient, HashiCorpVaultClient>();

        using var sp = services.BuildServiceProvider();
        var client = sp.GetRequiredService<IHashiCorpVaultClient>();

        // Observe activities created by the Vault ActivitySource using an ActivityListener
        var activities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == VaultTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => activities.Add(activity),
            ActivityStopped = activity => { }
        };

        // Register the listener; it will be unregistered when disposed at the end of the method
        ActivitySource.AddActivityListener(listener);

        // Call a method that should create an Activity. We don't expect HTTP success here,
        // but the activity should still be started by the code path before the HTTP call.
        try
        {
            await client.GetServiceAccessKeysAsync();
        }
        catch (System.Net.Http.HttpRequestException)
        {
            // Ignore HTTP errors; the test only asserts that an Activity was started before the HTTP call.
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation in CI or timeouts.
        }

        Assert.False(activities.IsEmpty, "Expected at least one Activity to be started by the Vault client.");
    }
}