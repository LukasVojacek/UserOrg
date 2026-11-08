using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ValueObjects;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IHeappeSynchronizationService"/> that records each command-template
/// sync trigger (project short name + resource name) so tests can assert per-resource invocation.
/// </summary>
internal sealed class FakeHeappeSynchronizationService : IHeappeSynchronizationService
{
    public List<(ProjectShortName ProjectShortName, string ResourceName)> Triggers { get; } = [];

    public Task TriggerSynchronizationForProjectResource(ProjectShortName projectShortName, string resourceName, CancellationToken cancellationToken)
    {
        Triggers.Add((projectShortName, resourceName));
        return Task.CompletedTask;
    }
}
