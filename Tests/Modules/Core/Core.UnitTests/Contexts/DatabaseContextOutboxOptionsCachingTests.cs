using Core.DataAccess.Contexts;
using Core.DataAccess.Events;
using Core.DataAccess.Outbox;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Contexts;

/// <summary>
/// Pins D2c of the create-resource-on-location-hang plan: <c>DatabaseContext.UnitOfWork.cs</c> must
/// build the <see cref="OutboxDbContext"/> options once per <see cref="System.Data.Common.DbConnection"/>
/// reference identity, not on every durable save. Exercises the caching method directly — no real SQL
/// Server connectivity is needed to prove the options object is reused, only that
/// <c>Database.GetDbConnection()</c> keeps returning the same connection instance for a scoped,
/// non-pooled context (which it does without ever opening a socket).
/// </summary>
public sealed class DatabaseContextOutboxOptionsCachingTests
{
    private static DatabaseContext NewContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseSqlServer("Server=fake;Database=fake;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        return new DatabaseContext(
            options,
            new DomainEventChannel(),
            Mock.Of<IRoleService>(),
            Options.Create(new Core.Domain.User.WellKnownRoles()),
            NullLogger<DatabaseContext>.Instance);
    }

    [Fact]
    public void Should_ReuseTheOutboxOptions_When_SavingSeveralTimes()
    {
        using var context = NewContext();

        // Calling through the outbox-options path several times simulates several durable saves on the
        // same DatabaseContext — each one previously built a fresh DbContextOptionsBuilder<OutboxDbContext>
        // from scratch. If that regressed, this would observe three DISTINCT option instances instead of
        // one — a real construction count, not a mock that would pass either way.
        var first = context.GetOutboxOptions();
        var second = context.GetOutboxOptions();
        var third = context.GetOutboxOptions();

        Assert.Same(first, second);
        Assert.Same(first, third);
    }

    [Fact]
    public void Should_RebuildTheOutboxOptions_When_TheUnderlyingConnectionInstanceChanges()
    {
        using var context = NewContext();
        var beforeReplace = context.GetOutboxOptions();

        // Swap the connection instance out from under the cache (a scoped, non-pooled DatabaseContext
        // never does this itself, but the cache must key on identity — not just memoize once forever —
        // so a changed connection is still honoured rather than silently reused).
        context.Database.SetDbConnection(new Microsoft.Data.SqlClient.SqlConnection(
            "Server=fake2;Database=fake2;Trusted_Connection=True;TrustServerCertificate=True;"));

        var afterReplace = context.GetOutboxOptions();

        Assert.NotSame(beforeReplace, afterReplace);
    }
}
