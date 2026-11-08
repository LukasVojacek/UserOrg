namespace Core.Domain.Project;

/// <summary>
/// Loads a <see cref="ProjectDomainType"/> lookup entity (tracked) by its unique name for assignment
/// to a project via <see cref="Project.ChangeDomainType"/>. Implemented by the write DbContext so the
/// returned entity is tracked by the same unit of work as the mutated aggregate.
/// </summary>
public interface IProjectDomainTypeLookup
{
    Task<ProjectDomainType?> FindByNameAsync(DomainTypeName name, CancellationToken cancellationToken);
}