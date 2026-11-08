using Core.DataAccess.Contexts;
using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;

namespace Core.UnitTests;

/// <summary>
/// Seeds the reference rows an integration fixture needs an FK value from, and deletes exactly those rows
/// again. A database migrated from zero carries none of them — no Core migration ships <c>InsertData</c> —
/// so a fixture that reads what happens to be there reports a vacuous skip or a vacuous pass on one.
/// </summary>
/// <remarks>
/// warn: every row is inserted, never borrowed from what the database already holds, and every name carries
/// the calling test's own token. Fixtures run in parallel and each deletes its own graph in a
/// <c>finally</c>, so a fixture that reuses a row it did not insert races that row's owner — an FK
/// violation under NO ACTION, and one that reproduces only on timing. Callers must filter on the identity
/// returned here rather than re-reading the table, or they reintroduce exactly that race.
/// </remarks>
internal sealed class ReferenceSeed(string token)
{
    private readonly List<(string Table, string IdColumn, object Id)> _seeded = [];

    /// <summary>The <c>UserName</c> (and <c>Email</c>) a <c>User</c> seeded under this token and suffix carries.</summary>
    internal string UserNameFor(string? suffix = null) => $"t093-{token}{suffix}@example.invalid";

    /// <summary>The <c>Name</c> and <c>ShortName</c> a <c>Project</c> seeded under this token and suffix carries.</summary>
    internal string ProjectShortNameFor(string? suffix = null) => $"t093-{token}{suffix}";

    /// <summary>The <c>Slug</c> a <c>Request</c> seeded under this token and suffix carries.</summary>
    internal string RequestSlugFor(string? suffix = null) => $"t093-{token}{suffix}";

    /// <summary>Seeds a row of an <c>(Id identity, Name, Description)</c> lookup table and returns its id and name.</summary>
    internal async Task<(int Id, string Name)> SeedLookupAsync(
        DatabaseContext context, string table, CancellationToken ct, string? suffix = null)
    {
        var name = $"t093-{token}{suffix}-{table}";
        await ExecuteAsync(context, $"INSERT INTO [{table}] ([Name], [Description]) VALUES (@p0, NULL)", ct, name);

        // why: recorded by name before the id is read back — a throw in that read would otherwise leave the
        // why: row inserted but unrecorded, and it would leak permanently on a shared database
        _seeded.Add((table, "Name", name));

        var id = await ScalarOrNullAsync<int>(context, $"SELECT [Id] AS [Value] FROM [{table}] WHERE [Name] = @p0", ct, name)
            ?? throw new InvalidOperationException($"The seeded [{table}] row could not be read back.");

        return (id, name);
    }

    /// <summary>
    /// Ensures the <c>LocationType</c> row whose id is <paramref name="locationType"/> exists and returns that type.
    /// The id is the closed enum value, not an identity a fresh insert could pick, so an existing row is used as
    /// it is and only a row inserted here is deleted again.
    /// </summary>
    internal async Task<ELocationType> SeedLocationTypeAsync(DatabaseContext context, ELocationType locationType, CancellationToken ct)
    {
        var inserted = await ExecuteAsync(
            context,
            """
            SET IDENTITY_INSERT [LocationType] ON;
            INSERT INTO [LocationType] ([Id], [Name], [Description])
            SELECT @p0, @p1, NULL WHERE NOT EXISTS (SELECT 1 FROM [LocationType] WHERE [Id] = @p0);
            SET IDENTITY_INSERT [LocationType] OFF;
            """,
            ct,
            (int)locationType,
            locationType.ToString());

        if (inserted > 0)
        {
            _seeded.Add(("LocationType", "Id", (int)locationType));
        }

        return locationType;
    }

    /// <summary>Seeds a <c>User</c> row named <see cref="UserNameFor"/> and returns its id.</summary>
    internal async Task<Guid> SeedUserAsync(DatabaseContext context, CancellationToken ct, string? suffix = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            context,
            """
            INSERT INTO [User] ([Id], [Email], [UserName], [AssignedProviderId], [CreatedAt], [ModifiedAt], [IsDeleted], [IdentityProviderUserId])
            VALUES (@p0, @p1, @p1, NULL, SYSUTCDATETIME(), NULL, 0, @p1)
            """,
            ct,
            id,
            UserNameFor(suffix));

        _seeded.Add(("User", "Id", id));
        return id;
    }

    /// <summary>Seeds a <c>Provider</c> row, with the <c>Country</c> its FK needs, and returns its id and name.</summary>
    internal async Task<(Guid Id, string Name)> SeedProviderAsync(DatabaseContext context, CancellationToken ct, string? suffix = null)
    {
        var (countryId, _) = await SeedLookupAsync(context, "Country", ct, suffix);
        var name = $"t093-{token}{suffix}-provider";
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            context,
            """
            INSERT INTO [Provider] ([Id], [Name], [Description], [IsPrivate], [CountryId], [CreatedAt], [ModifiedAt],
                                    [IsDeleted], [SupportEmail], [ExternalId], [Mode], [ServiceName])
            VALUES (@p0, @p1, NULL, 0, @p2, SYSUTCDATETIME(), NULL, 0, NULL, NULL, 0, @p1)
            """,
            ct,
            id,
            name,
            countryId);

        _seeded.Add(("Provider", "Id", id));
        return (id, name);
    }

    /// <summary>Seeds a <c>Project</c> row owned by this token and returns its id.</summary>
    internal async Task<Guid> SeedProjectAsync(
        DatabaseContext context, Guid createdByUserId, int projectDomainTypeId, CancellationToken ct, string? suffix = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            context,
            """
            INSERT INTO [Project] ([Id], [Name], [ShortName], [Description], [Email], [StartDate], [EndDate],
                                   [ProjectDomainTypeId], [CreatedByUserId], [CreatedAt], [ModifiedAt], [IsDeleted],
                                   [ExternalId], [Mode], [ServiceName])
            VALUES (@p0, @p1, @p1, NULL, NULL, SYSUTCDATETIME(), SYSUTCDATETIME(), @p2, @p3, SYSUTCDATETIME(), NULL, 0, NULL, 0, @p1)
            """,
            ct,
            id,
            ProjectShortNameFor(suffix),
            projectDomainTypeId,
            createdByUserId);

        _seeded.Add(("Project", "Id", id));
        return id;
    }

    /// <summary>Seeds a <c>Request</c> row with a non-null <c>ContactEmail</c> and returns its id.</summary>
    /// <remarks>invariant: State 1 and Type 1 are inside the ranges RequestState (1-5) and RequestType (0-10) accept when EF materializes the row.</remarks>
    internal async Task<Guid> SeedRequestAsync(
        DatabaseContext context, Guid userId, Guid projectId, CancellationToken ct, string? suffix = null)
    {
        var id = Guid.CreateVersion7();
        await ExecuteAsync(
            context,
            """
            INSERT INTO [Request] ([Id], [UserId], [ProjectId], [ProjectResourceId], [State], [Type], [CreatedAt],
                                   [ModifiedAt], [IsDeleted], [ContactEmail], [ProviderId], [ProjectResourceName], [Slug])
            VALUES (@p0, @p1, @p2, NULL, 1, 1, SYSUTCDATETIME(), NULL, 0, @p3, NULL, NULL, @p4)
            """,
            ct,
            id,
            userId,
            projectId,
            UserNameFor(suffix),
            RequestSlugFor(suffix));

        _seeded.Add(("Request", "Id", id));
        return id;
    }

    /// <summary>
    /// Soft-deletes a row this instance seeded by writing <c>IsDeleted = 1</c> straight to the column.
    /// </summary>
    /// <remarks>
    /// why: the aggregate's own Delete() would route through the domain and its repository, so a test using
    /// it would exercise that path instead of the EF query filter it means to assert on.
    /// </remarks>
    internal Task SoftDeleteAsync(DatabaseContext context, string table, Guid id, CancellationToken ct)
    {
        // invariant: only a row this instance seeded may be soft-deleted; a foreign row would stay flipped
        if (!_seeded.Contains((table, "Id", id)))
        {
            throw new InvalidOperationException($"[{table}] row {id} was not seeded by this fixture.");
        }

        return ExecuteAsync(context, $"UPDATE [{table}] SET [IsDeleted] = 1 WHERE [Id] = @p0", ct, id);
    }

    /// <summary>
    /// Deletes exactly the rows this instance inserted, newest first, so a row that references an earlier
    /// one goes before the row it points at (every FK in this schema is NO ACTION).
    /// </summary>
    internal async Task DeleteSeededAsync(DatabaseContext context)
    {
        // warn: every row is attempted even when an earlier DELETE fails, so one FK still held does not
        // warn: abandon the rows behind it; the caller's own token is gone, so the message has to name them
        // warn: ct is deliberately not passed — a cancelled or timed-out test would abort the first DELETE
        // warn: and leak the whole graph, which is the failure this loop exists to prevent
        List<Exception>? failures = null;
        for (var i = _seeded.Count - 1; i >= 0; i--)
        {
            var (table, keyColumn, key) = _seeded[i];
            try
            {
                await ExecuteAsync(context, $"DELETE FROM [{table}] WHERE [{keyColumn}] = @p0", CancellationToken.None, key);
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                (failures ??= []).Add(
                    new InvalidOperationException($"[{table}] row with [{keyColumn}] = {key} was left behind.", failure));
            }
        }

        _seeded.Clear();

        if (failures is not null)
        {
            throw new AggregateException("Seeded reference rows were left behind.", failures);
        }
    }

    // why: the interpolated EF overloads would turn the table name into a parameter, which SQL Server rejects where an identifier is expected — here every identifier is a literal of this file and every value stays a parameter
    private static Task<int> ExecuteAsync(DatabaseContext context, string sql, CancellationToken ct, params object[] parameters) =>
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — identifiers are fixture literals, values are parameters.
        context.Database.ExecuteSqlRawAsync(sql, parameters, ct);
#pragma warning restore EF1002

    private static async Task<T?> ScalarOrNullAsync<T>(
        DatabaseContext context, string sql, CancellationToken ct, params object[] parameters) where T : struct
    {
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection — identifiers are fixture literals, values are parameters.
        var rows = await context.Database.SqlQueryRaw<T>(sql, parameters).ToListAsync(ct);
#pragma warning restore EF1002
        return rows.Count == 0 ? null : rows[0];
    }
}