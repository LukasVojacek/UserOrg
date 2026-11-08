using Core.DataAccess.Queries.Contracts;

using RemI.Features.Projects;
using RemI.Features.Users;

namespace Userorg.Adapter.RemI.Userorg;

internal static class ModelExtensions
{
    internal static UserResponse ConvertToModel(this UserWithIdSummary user) => new(
        user.UserId.Value.ToString(),
        user.IdentityProviderId.Value,
        user.Email.Value,
        user.UserName.Value,
        [.. user.Projects.Select(p => p.Value)],
        user.AssignedProviderName?.Value);

    internal static ProjectResponse ConvertToModel(this ProjectSummary project) => new(
        project.ShortName.Value,
        project.Name.Value,
        project.Email?.Value,
        project.ExternalId?.Value,
        project.Mode.ToString(),
        project.ServiceName.Value,
        project.StartDate,
        project.EndDate);
}