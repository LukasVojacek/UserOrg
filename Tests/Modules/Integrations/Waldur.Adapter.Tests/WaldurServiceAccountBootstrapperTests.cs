using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Userorg.Adapter.Waldur.Sync;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;

namespace Waldur.Adapter.Tests;

/// <summary>
/// Tests for <see cref="WaldurServiceAccountBootstrapper"/> (ADR-0057/A5, D18): every identity must be
/// attempted in its own DI scope, and per-identity failures must be aggregated rather than aborting
/// the remaining identities.
/// </summary>
public sealed class WaldurServiceAccountBootstrapperTests
{
    /// <summary>
    /// <see cref="IUserOrgDbContext"/> test double that records the service-account save attempt for
    /// every scope and simulates a save failure for configured service names.
    /// </summary>
    private sealed class FailingUserOrgDbContext(
        DbContextOptions<DatabaseContext> options,
        HashSet<string> failingServiceNames,
        List<string> attempts) : DatabaseContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var addedServiceNames = ChangeTracker.Entries<User>()
                .Where(e => e.State == EntityState.Added)
                .Select(e => e.Entity.IdentityProviderUserId)
                .ToList();

            attempts.AddRange(addedServiceNames);

            if (addedServiceNames.Exists(failingServiceNames.Contains))
            {
                throw new DbUpdateException("Simulated save failure.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    private static DbContextOptions<DatabaseContext> NewInMemoryOptions() =>
        new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static WaldurServiceAccountBootstrapper BuildBootstrapper(
        IEnumerable<WaldurServiceAccountIdentity> identities,
        HashSet<string> failingServiceNames,
        List<string> attempts)
    {
        var services = new ServiceCollection();
        services.AddScoped<IUserOrgDbContext>(_ =>
            new FailingUserOrgDbContext(NewInMemoryOptions(), failingServiceNames, attempts));
        var provider = services.BuildServiceProvider();

        return new WaldurServiceAccountBootstrapper(
            identities,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WaldurServiceAccountBootstrapper>.Instance);
    }

    [Fact(DisplayName = "StartAsync attempts every identity even after an earlier one fails")]
    public async Task Should_AttemptAllIdentities_When_AnEarlierIdentityFails()
    {
        var failingServiceNames = new HashSet<string> { "svc-fail-1" };
        var attempts = new List<string>();
        WaldurServiceAccountIdentity[] identities =
        [
            new("svc-fail-1", "user1", "svc1@example.com"),
            new("svc-ok-2", "user2", "svc2@example.com"),
            new("svc-ok-3", "user3", "svc3@example.com"),
        ];
        var bootstrapper = BuildBootstrapper(identities, failingServiceNames, attempts);

        await Assert.ThrowsAsync<AggregateException>(() => bootstrapper.StartAsync(CancellationToken.None));

        Assert.Equal(["svc-fail-1", "svc-ok-2", "svc-ok-3"], attempts);
    }

    [Fact(DisplayName = "StartAsync aggregates every per-identity failure into a single AggregateException")]
    public async Task Should_AggregateAllFailures_When_MultipleIdentitiesFail()
    {
        var failingServiceNames = new HashSet<string> { "svc-fail-1", "svc-fail-3" };
        var attempts = new List<string>();
        WaldurServiceAccountIdentity[] identities =
        [
            new("svc-fail-1", "user1", "svc1@example.com"),
            new("svc-ok-2", "user2", "svc2@example.com"),
            new("svc-fail-3", "user3", "svc3@example.com"),
        ];
        var bootstrapper = BuildBootstrapper(identities, failingServiceNames, attempts);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => bootstrapper.StartAsync(CancellationToken.None));

        Assert.Equal(2, ex.InnerExceptions.Count);
        Assert.All(ex.InnerExceptions, e => Assert.IsType<InvalidOperationException>(e));
        Assert.Contains(ex.InnerExceptions, e => e.Message.Contains("svc-fail-1"));
        Assert.Contains(ex.InnerExceptions, e => e.Message.Contains("svc-fail-3"));
        Assert.DoesNotContain(ex.InnerExceptions, e => e.Message.Contains("svc-ok-2"));
    }

    [Fact(DisplayName = "StartAsync completes without throwing when every identity bootstraps successfully")]
    public async Task Should_CompleteWithoutThrowing_When_AllIdentitiesSucceed()
    {
        var attempts = new List<string>();
        WaldurServiceAccountIdentity[] identities =
        [
            new("svc-ok-1", "user1", "svc1@example.com"),
            new("svc-ok-2", "user2", "svc2@example.com"),
        ];
        var bootstrapper = BuildBootstrapper(identities, [], attempts);

        await bootstrapper.StartAsync(CancellationToken.None);

        Assert.Equal(["svc-ok-1", "svc-ok-2"], attempts);
    }
}
