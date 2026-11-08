using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

internal sealed partial class DatabaseContext : IRepository<Domain.Request.Request, Email>
{
    private IQueryable<Domain.Request.Request> _requestIncludes => Requests
        .AsSplitQuery()
        .Include("_requestActionLinks");

    async ValueTask<Domain.Request.Request?> IRepository<Domain.Request.Request, Email>.AddAsync(Domain.Request.Request aggregate, CancellationToken cancellationToken)
    {
        // Request.Id (shadow Guid PK) is stamped with a UUID v7 by UuidV7ValueGenerator on Add; not assigned here.
        await Requests.AddAsync(aggregate, cancellationToken);
        RegisterAddedAggregateRoot(aggregate);
        return await Task.FromResult(aggregate);
    }

    void IRepository<Domain.Request.Request, Email>.Delete(Domain.Request.Request aggregate, CancellationToken cancellationToken) => Requests.Remove(aggregate);

    async ValueTask<Domain.Request.Request?> IRepository<Domain.Request.Request, Email>.FindAsync(Email key, CancellationToken cancellationToken) => await _requestIncludes
        .FirstOrDefaultAsync(r => r.ContactEmail == key, cancellationToken);
}

/// <summary>
/// Tracked load of a Request by its surrogate <see cref="RequestId"/> (the EF shadow PK) for the write
/// path — accept / reject / handle arrive with the request id, not the natural <see cref="Email"/> key.
/// </summary>
internal sealed partial class DatabaseContext : IRepository<Domain.Request.Request, RequestId>
{
    async ValueTask<Domain.Request.Request?> IRepository<Domain.Request.Request, RequestId>.AddAsync(Domain.Request.Request aggregate, CancellationToken cancellationToken)
    {
        // Request.Id (shadow Guid PK) is stamped with a UUID v7 by UuidV7ValueGenerator on Add; not assigned here.
        await Requests.AddAsync(aggregate, cancellationToken);
        RegisterAddedAggregateRoot(aggregate);
        return aggregate;
    }

    void IRepository<Domain.Request.Request, RequestId>.Delete(Domain.Request.Request aggregate, CancellationToken cancellationToken) => Requests.Remove(aggregate);

    async ValueTask<Domain.Request.Request?> IRepository<Domain.Request.Request, RequestId>.FindAsync(RequestId key, CancellationToken cancellationToken) => await _requestIncludes
        .FirstOrDefaultAsync(r => EF.Property<Guid>(r, "Id") == key.Value, cancellationToken);
}