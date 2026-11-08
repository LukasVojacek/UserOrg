namespace Core.Features.Project.GetProject;

internal sealed record ProjectUserRecord(string UserName, string Email);
internal sealed record ResourceDetailRecord(string ResourceName, DateTimeOffset StartDate, DateTimeOffset EndDate);

internal sealed record GetProjectResponse(
    string ShortName,
    string Name,
    string? Description,
    string? Email,
    string? ExternalId,
    string Mode,
    string State,
    string ServiceName,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string DomainType,
    bool IsDeleted,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ProjectUserRecord> Users,
    IReadOnlyList<ResourceDetailRecord> Resources)
{
    internal static GetProjectResponse From(ProjectDetails project) => new(
        project.ShortName.Value,
        project.Name.Value,
        project.Description?.Value,
        project.Email?.Value,
        project.ExternalId?.Value,
        project.Mode.ToString(),
        project.State.ToString(),
        project.ServiceName.Value,
        project.StartDate,
        project.EndDate,
        project.DomainType.Value,
        project.IsDeleted,
        [.. project.Tags.Select(t => t.Value)],
        [.. project.Users.Select(u => new ProjectUserRecord(u.UserName.Value, u.Email.Value))],
        [.. project.Resources.Select(r => new ResourceDetailRecord(r.Name.Value, r.StartDate, r.EndDate))]
    );
}