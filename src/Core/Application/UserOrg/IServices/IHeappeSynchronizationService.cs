using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.IServices;

public interface IHeappeSynchronizationService
{
    Task TriggerSynchronizationForProjectResource(ProjectShortName projectShortName, string resourceName, CancellationToken cancellationToken);
}