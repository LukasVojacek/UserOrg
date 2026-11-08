namespace Core.Domain.Project;

/// <summary>Marker for domain events scoped to a single project, exposing the project's <see cref="ProjectShortName"/>.</summary>
public interface IProjectScopedEvent
{
    ProjectShortName ShortName { get; }
}

/// <summary>Domain event raised when a new project is created.</summary>
public sealed record ProjectCreatedEvent(ProjectShortName ShortName, ProjectName Name, UserId CreatedBy) : IDomainEvent, IProjectScopedEvent;

/// <summary>Domain event raised when a project is soft-deleted.</summary>
public sealed record ProjectDeletedEvent(ProjectShortName ShortName) : IDomainEvent, IProjectScopedEvent;

/// <summary>
/// Domain event raised when a resource is assigned to or removed from the project (the resource↔project link
/// changed). Carries the affected project's <see cref="ShortName"/> and the resource's <see cref="ResourceName"/>
/// so the post-commit handler enqueues exactly one HEAppE sync for that project (ADR-0016; outbox in F6).
/// </summary>
public sealed record ProjectResourceAssignmentChangedEvent(ProjectShortName ShortName, ResourceName ResourceName) : IDurableEvent, IProjectScopedEvent;