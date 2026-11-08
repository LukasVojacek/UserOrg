using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

using UserOrganizationManagement.Application.Abstraction;

namespace Userorg.Adapter.Waldur.Sync;

/// <summary>
/// Startup bootstrap for every enabled instance's Waldur service account (ADR-0057/A5, D18). Runs
/// once before the host starts accepting/publishing events; each identity gets its OWN DI scope
/// (fresh <see cref="IUserOrgDbContext"/>) so one instance's failure does not affect another's.
/// </summary>
internal sealed class WaldurServiceAccountBootstrapper(
    IEnumerable<WaldurServiceAccountIdentity> identities,
    IServiceScopeFactory scopeFactory,
    ILogger<WaldurServiceAccountBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var identity in identities)
        {
            using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<IUserOrgDbContext>();
            var result = await context.InitializeSystemExportUser(logger, identity, cancellationToken);
            if (!result.IsSuccess)
            {
                failures.Add(new InvalidOperationException($"Failed to bootstrap service account for '{identity.ServiceName}': {result.Error}"));
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Failed to bootstrap one or more Waldur service accounts.", failures);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}