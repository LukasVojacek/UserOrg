namespace Core.Domain.Project;

/// <summary>
/// Resolves a project's natural key (<see cref="ProjectShortName"/>) from its surrogate
/// <see cref="ProjectId"/> on the write path — e.g. to scope-check and load a project referenced by a
/// <c>Request</c>, which holds the id but not the short name. Lightweight projection; does not hydrate
/// the full <see cref="Project"/> aggregate.
/// </summary>
public interface IProjectDirectory
{
    Task<ProjectShortName?> FindShortNameAsync(ProjectId projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a project's surrogate <see cref="ProjectId"/> from its natural <see cref="ProjectShortName"/>
    /// — needed to stamp/cross-reference a <c>Request</c>, which holds the id, not the short name.
    /// </summary>
    Task<ProjectId?> FindIdAsync(ProjectShortName shortName, CancellationToken cancellationToken);
}