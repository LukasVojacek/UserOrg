using Lukdrasil.Error;
using Lukdrasil.Result;

namespace Core.Domain.Project;

/// <summary>
/// ProjectTag — shared catalogue entry (Aggregate Root, natural identifier: <see cref="Name"/>).
///
/// A Tag is a single global catalogue entry assigned to many projects (M:N), not a per-project value,
/// so a catalogue-level mutation (delete) goes through this root rather than through <c>Project</c>.
/// Assignment to a project is still driven by the Project aggregate's <c>_projectTags</c> skip navigation.
/// </summary>
public sealed class ProjectTag : AggregateRoot, ISoftDeletableEntity
{
    public TagName Name { get; private set; }
    public UserName CreatedBy { get; private set; }

    /// <summary>Soft delete flag. Maps to [IsDeleted] column.</summary>
    public bool IsDeleted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ModifiedAt { get; private set; }

    /// <summary>
    /// Creates a new active catalogue tag with the given name, owned (for audit) by <paramref name="createdBy"/>.
    /// Public because the create-tag use-case in the <c>Core</c> project constructs the aggregate and hands it
    /// to <see cref="IRepository{ProjectTag, TagName}.AddAsync"/> (which applies ensure/restore semantics),
    /// mirroring the public <c>Project.Create</c> / <c>ProjectResource.Create</c> factories. The surrogate
    /// shadow PK and <c>CreatedByUserId</c> FK are assigned by the data-access adapter on insert.
    /// </summary>
    public static ProjectTag Create(TagName tagName, UserName createdBy)
    {
        return new ProjectTag
        {
            Name = tagName,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Soft-deletes the catalogue entry. Idempotent guard: a second delete fails with
    /// <see cref="ProjectTagErrors.AlreadyDeleted"/>. The cross-aggregate in-use check (is any active
    /// project still assigned this tag?) lives in the use-case, not here. Raises no domain event — deleting
    /// an unused tag touches no project, and the tag listing is not cached.
    /// </summary>
    public Result<Error> Delete()
    {
        if (IsDeleted)
        {
            return Result<Error>.Failure(ProjectTagErrors.AlreadyDeleted(Name));
        }

        IsDeleted = true;
        ModifiedAt = DateTimeOffset.UtcNow;
        return Result<Error>.Success();
    }

    /// <summary>
    /// Clears the soft-delete flag, reviving the catalogue entry. Synchronous and idempotent: calling it
    /// on an already-active tag is a no-op. Mirror of <see cref="Delete"/>; used by the repository ensure
    /// path to revive a soft-deleted row on a same-name create instead of inserting a duplicate.
    /// </summary>
    public void Restore()
    {
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        ModifiedAt = DateTimeOffset.UtcNow;
    }
}