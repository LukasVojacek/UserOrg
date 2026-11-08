using Core.DataAccess.Contexts;
using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests;

/// <summary>
/// Regression tests for the audit-stamping convention in <c>DatabaseContext.ApplyConventions</c>,
/// which writes <see cref="DateTimeOffset.UtcNow"/> into the shadow <c>ModifiedAt</c> property of every
/// modified entity. Every entity now maps <c>ModifiedAt</c> as <c>DateTimeOffset?</c> through the converter,
/// and <see cref="Should_MapEveryModifiedAtAsDateTimeOffset_When_ModelIsBuilt"/> guards the model against a
/// raw <c>DateTime?</c> mapping coming back. Run over the in-memory provider: a type mismatch is rejected by the
/// change tracker when <c>CurrentValue</c> is assigned, before any SQL is produced, so a live database is not
/// required to reproduce it.
/// </summary>
public sealed class AuditStampingTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;

    public AuditStampingTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DatabaseContext>());

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_StampModifiedAt_When_ProviderIsUpdated()
    {
        // Provider used to map ModifiedAt as a raw DateTime? column — the case that crashed when the convention
        // assigned a DateTimeOffset into it.
        await SeedProviderAsync("acme");

        DateTimeOffset? modifiedAt = await MutateAndSaveAsync<ProviderAggregate, ProviderName>(
            ProviderName.From("acme"),
            provider => provider.SetPrivacy(true));

        Assert.NotNull(modifiedAt);
    }

    [Fact]
    public async Task Should_StampModifiedAt_When_DateTimeOffsetColumnEntityIsUpdated()
    {
        // Project, a second aggregate with its own key type, gets the same shadow DateTimeOffset? stamp through
        // the converter as Provider does.
        await SeedProjectAsync("proj-x");

        DateTimeOffset? modifiedAt = await MutateAndSaveAsync<ProjectAggregate, ProjectShortName>(
            new ProjectShortName("proj-x"),
            project => project.Rename(ProjectName.Create("renamed")));

        Assert.NotNull(modifiedAt);
    }

    [Fact]
    public void Should_MapEveryModifiedAtAsDateTimeOffset_When_ModelIsBuilt()
    {
        // The stamp assigns a DateTimeOffset into every Modified entry's ModifiedAt, so any entity that still
        // maps it as DateTime? throws InvalidCastException on save (unassigning a project resource did).
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var offenders = context.Model.GetEntityTypes()
            .Select(entityType => (Entity: entityType.DisplayName(), ModifiedAt: entityType.FindProperty("ModifiedAt")))
            .Where(mapping => mapping.ModifiedAt is not null && mapping.ModifiedAt.ClrType != typeof(DateTimeOffset?))
            .Select(mapping => $"{mapping.Entity}: {mapping.ModifiedAt!.ClrType}")
            .ToList();

        Assert.Empty(offenders);
    }

    private async Task<DateTimeOffset?> MutateAndSaveAsync<TAggregate, TKey>(
        TKey key,
        Action<TAggregate> mutate)
        where TAggregate : AggregateRoot
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var repository = uow.GetRepository<TAggregate, TKey>();
        var aggregate = await repository.FindAsync(key, ct);
        Assert.NotNull(aggregate);

        mutate(aggregate);

        // The convention only runs through IUnitOfWork.SaveChangesAsync, not base SaveChangesAsync.
        await uow.SaveChangesAsync(ct);

        return (DateTimeOffset?)context.Entry(aggregate).Property("ModifiedAt").CurrentValue;
    }

    private async Task SeedProviderAsync(string name)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From(name), countryId: 1);
        context.Add(provider);
        await context.SaveChangesAsync(ct);
    }

    private async Task SeedProjectAsync(string shortName)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var ct = TestContext.Current.CancellationToken;
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = ProjectAggregate.Create(
            new ProjectShortName(shortName),
            ProjectName.Create(shortName),
            ProjectDomainType.FromName(DomainTypeName.Create($"domain-{shortName}")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);
        context.Add(project);
        context.Entry(project).Property("Id").CurrentValue = ProjectId.From(Guid.NewGuid());

        var domainType = context.ChangeTracker.Entries<ProjectDomainType>().Single();
        var key = domainType.Metadata.FindPrimaryKey()!.Properties[0].Name;
        domainType.Property(key).CurrentValue = new ProjectDomainTypeId(1000);

        await context.SaveChangesAsync(ct);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
