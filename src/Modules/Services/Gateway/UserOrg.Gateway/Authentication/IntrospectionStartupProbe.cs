using UserOrg.Gateway.Startup;

namespace UserOrg.Gateway.Authentication;

internal sealed class IntrospectionStartupProbe(
    IServiceProvider services,
    IConfiguration configuration,
    ILogger<IntrospectionStartupProbe> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogBearerAuthorityConfigured(configuration["Idp:Authority"]);
        services.GetRequiredService<ITokenIntrospectionService>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}