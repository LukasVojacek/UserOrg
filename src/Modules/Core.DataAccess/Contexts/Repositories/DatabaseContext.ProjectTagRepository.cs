using Microsoft.EntityFrameworkCore;

using ProjectTagAggregate = Core.Domain.Project.ProjectTag;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Write port for the shared <see cref="ProjectTagAggregate"/> catalogue. Addressed by its natural key
/// (<see cref="TagName"/>); the surrogate Id is an EF shadow PK. <c>FindAsync</c> excludes soft-deleted
/// entries so a deleted tag reads as missing (the delete endpoint maps that to 404). Mirrors
/// <c>DatabaseContext.ProviderRepository</c>.
/// </summary>
internal sealed partial class DatabaseContext : IRepository<ProjectTagAggregate, TagName>
{
    async ValueTask<ProjectTagAggregate?> IRepository<ProjectTagAggregate, TagName>.FindAsync(TagName key, CancellationToken cancellationToken) =>
        await Set<ProjectTagAggregate>()
            .FirstOrDefaultAsync(t => t.Name == key && !t.IsDeleted, cancellationToken);

    /// <summary>
    /// Ensure-add on the natural key <see cref="TagName"/>. Looks up an existing row by <c>Name</c>
    /// <b>including soft-deleted</b> rows: any hit is necessarily soft-deleted (an active row would already
    /// have been returned by the caller's prior <c>FindAsync</c>), so the row is <c>Restore()</c>d and the
    /// tracked aggregate returned instead of inserting a duplicate. Concurrent first-time creates of the
    /// same name can still race into duplicate rows until the deferred unique index on
    /// <c>ProjectTag.Name</c> lands (same window as the replaced handler). On a genuine new name the
    /// surrogate shadow PK (<c>Id</c>) is
    /// minted and the <c>CreatedByUserId</c> shadow FK is resolved from the aggregate's <c>CreatedBy</c>
    /// user name; a missing creator is a programming error (the endpoint 404s on a missing actor first)
    /// and throws <see cref="InvalidOperationException"/>.
    /// </summary>
    async ValueTask<ProjectTagAggregate?> IRepository<ProjectTagAggregate, TagName>.AddAsync(ProjectTagAggregate aggregate, CancellationToken cancellationToken)
    {
        var existing = await Set<ProjectTagAggregate>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Name == aggregate.Name, cancellationToken);
        if (existing is not null)
        {
            existing.Restore();
            return existing;
        }

        // ProjectTag.CreatedBy (UserName) is ignored by EF; the table requires the CreatedByUserId
        // shadow FK, so resolve the creator's surrogate id and set it on the new entity.
        var creator = await Set<Domain.User.User>().FirstOrDefaultAsync(u => u.UserName == aggregate.CreatedBy, cancellationToken)
            ?? throw new InvalidOperationException($"User '{aggregate.CreatedBy.Value}' not found while creating tag '{aggregate.Name.Value}'.");

        var entry = await Set<ProjectTagAggregate>().AddAsync(aggregate, cancellationToken);
        entry.Property("Id").CurrentValue = Guid.CreateVersion7();
        entry.Property("CreatedByUserId").CurrentValue = Entry(creator).Property<UserId>("Id").CurrentValue;
        RegisterAddedAggregateRoot(aggregate);
        return aggregate;
    }

    void IRepository<ProjectTagAggregate, TagName>.Delete(ProjectTagAggregate aggregate, CancellationToken cancellationToken) =>
        Set<ProjectTagAggregate>().Remove(aggregate);
}