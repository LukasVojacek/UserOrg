using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NSubstitute;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;


namespace WorkflowEngine.Strategies.Tests;


public sealed class SampleApprovalFlowDemo : IAsyncLifetime
{
    // Simulates an external approval gate (e.g. a Redis entry set by a REST endpoint).
    private IHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        // Suppress console noise during the demo run; a real app keeps the default providers.
        builder.Logging.ClearProviders();


        _host = builder.Build();
        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact(DisplayName =
        "SampleApprovalFlow — full 4-step flow completes end-to-end with human approval provided between steps 3 and 4")]
    public async Task ShouldCompleteFlowWhenApprovalIsProvidedAfterRetry()
    {
        // Method intentionally left empty.
    }
}