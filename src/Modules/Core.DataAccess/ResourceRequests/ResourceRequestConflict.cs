using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Which uniqueness guard a projection-row insert violated (ADR-0066). Lets the create endpoint map a database
/// race to the right HTTP outcome instead of a 500: a lost single-active race is a 409, a slug-PK race is a
/// recompute-and-retry.
/// </summary>
public enum ResourceRequestConflict
{
    /// <summary>The filtered unique index on (project, provider, resource) — another non-terminal request won the race.</summary>
    SingleActive,

    /// <summary>The slug primary key / unique constraint — a concurrent insert took the chosen slug.</summary>
    Slug,
}

/// <summary>
/// Raised by <see cref="IResourceRequestProjectionStore.InsertAsync"/> when the insert loses a uniqueness race,
/// carrying the specific <see cref="ResourceRequestConflict"/> so the caller can map it deterministically. Any
/// other <see cref="DbUpdateException"/> is left to propagate unchanged.
/// </summary>
public sealed class ResourceRequestConflictException : Exception
{
    public ResourceRequestConflictException(ResourceRequestConflict conflict, Exception innerException)
        : base($"Resource-request insert violated the {conflict} uniqueness guard.", innerException) =>
        Conflict = conflict;

    public ResourceRequestConflict Conflict { get; }
}

/// <summary>
/// Maps a SQL <see cref="DbUpdateException"/> from a projection-row insert to the uniqueness guard it violated by
/// matching the offending constraint/index name (the EF-generated names on the <c>ResourceRequests</c> table).
/// String-name matching is the pragmatic SQL-Server contract; the names are asserted stable by the migration.
/// </summary>
public static class ResourceRequestConstraintClassifier
{
    /// <summary>The filtered single-active unique index (EF default name for the 3-column index).</summary>
    public const string SingleActiveIndexName = "IX_ResourceRequests_ProjectShortName_ProviderName_ResourceName";

    /// <summary>The slug primary key.</summary>
    public const string SlugPrimaryKeyName = "PK_ResourceRequests";

    /// <summary>
    /// Classifies <paramref name="exception"/>, or returns <c>null</c> when it is not a recognised uniqueness
    /// violation (the caller should then rethrow).
    /// </summary>
    public static ResourceRequestConflict? Classify(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception.InnerException?.Message ?? exception.Message;

        if (message.Contains(SingleActiveIndexName, StringComparison.Ordinal))
        {
            return ResourceRequestConflict.SingleActive;
        }

        if (message.Contains(SlugPrimaryKeyName, StringComparison.Ordinal))
        {
            return ResourceRequestConflict.Slug;
        }

        return null;
    }
}