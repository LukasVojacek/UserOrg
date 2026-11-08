using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace Waldur.Adapter.Tests.Fakes;

/// <summary>
/// Test double for <see cref="UserOrganizationManagement.Application.Abstraction.IUserOrgDbContext"/> backed by the
/// real <see cref="DatabaseContext"/> with the EF Core InMemory provider.
/// Each instance uses a unique database name so tests are fully isolated from one another.
/// </summary>
internal sealed class FakeUserOrgDbContext : DatabaseContext
{
    public FakeUserOrgDbContext()
        : base(new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options)
    { }

    /// <summary>Adds <paramref name="entities"/> to the in-memory store and saves immediately.</summary>
    public void Seed<T>(IEnumerable<T> entities) where T : class
    {
        AddRange(entities);
        SaveChanges();
    }
}
