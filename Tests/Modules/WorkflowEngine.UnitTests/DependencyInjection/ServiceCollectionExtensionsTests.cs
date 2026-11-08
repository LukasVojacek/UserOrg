using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Domain.Request;
using WorkflowEngine.Core.Ports;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.DependencyInjection;

/// <summary>
/// Pins the DI lifetime contract for the hosted workers: production registers
/// <see cref="IRequestRepository"/> as scoped, so singleton hosted services must resolve it through
/// an <see cref="IServiceScopeFactory"/>-created scope rather than the constructor. A regression here
/// crashes the host under <c>ValidateScopes</c> (e.g. the Aspire E2E fixture).
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    private sealed class NoopCallbackPort : ICallbackPort
    {
        public Task SendAsync(RequestAggregate aggregate, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact(DisplayName = "Hosted workers construct without scope-validation errors when IRequestRepository is scoped")]
    public void Should_BuildAndConstructHostedServices_When_ValidatingScopes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IRequestRepository, InMemoryRequestRepository>();
        services.AddSingleton<ICallbackPort, NoopCallbackPort>();
        services.AddWorkflowEngineApplicationServices();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var hostedServices = provider.GetServices<IHostedService>().ToList();

        Assert.NotEmpty(hostedServices);
    }
}
