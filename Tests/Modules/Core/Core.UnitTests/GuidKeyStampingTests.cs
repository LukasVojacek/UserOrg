using Core.DataAccess.Contexts;
using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using ProjectAggregate = Core.Domain.Project.Project;
using ProviderAggregate = Core.Domain.Provider.Provider;

namespace Core.UnitTests;

/// <summary>
/// Characterization tests for <c>AuditableEntityConventions.ApplyCodeGeneratedGuidKeys</c>
/// (wired into the modular <see cref="DatabaseContext"/>). Pins today's behavior ahead of the
/// #246/ADR-0061 relocation of <c>UuidV7ValueGenerator</c> into <c>UserOrg.Shared</c>: the
/// relocation must be a pure move with zero behavior change over the modular context, so these
/// tests must stay green before and after.
///
/// Unlike <see cref="AuditStampingTests"/>'s <c>SeedProjectAsync</c> (which overwrites
/// <c>Project.Id</c> right after <c>Add</c> to sidestep an unrelated in-memory-provider quirk with
/// the <c>ProjectDomainType</c> int key), these tests never touch the aggregate's own Guid PK —
/// they let the convention's value generator stamp it, so a broken/misrouted convention would
/// actually turn them red.
/// </summary>
public sealed class GuidKeyStampingTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _serviceProvider;

    public GuidKeyStampingTests()
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
    public async Task Should_StampNonEmptyUuidV7Id_When_ShadowGuidPkAggregateIsAdded()
    {
        // Provider's PK is a shadow-property ProviderId (Guid-backed, via converter) with no CLR
        // property on the aggregate itself — the plain "single Guid PK, no typed conversion at the
        // property level" case the convention targets.
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var provider = ProviderAggregate.Create(ProviderName.From("stamped-provider"), countryId: 1);
        context.Add(provider);
        await context.SaveChangesAsync(ct);

        var stampedId = (ProviderId)context.Entry(provider).Property("Id").CurrentValue!;

        Assert.NotEqual(Guid.Empty, stampedId.Value);
    }

    [Fact]
    public async Task Should_StampNonEmptyUuidV7Id_When_ProjectAggregateWithConvertedIdIsAdded()
    {
        // Project.Id is exposed as the typed ProjectId value object via .HasConversion — the
        // converter round-trip case the convention must also cover (NextValue maps the raw Guid
        // through the property's ValueConverter before it lands on the CLR property).
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var project = ProjectAggregate.Create(
            new ProjectShortName("stamped-proj"),
            ProjectName.Create("stamped-proj"),
            ProjectDomainType.FromName(DomainTypeName.Create("domain-stamped-proj")),
            UserId.New(),
            Start,
            End,
            ServiceName.Default);
        context.Add(project);

        // Unrelated to the Guid-PK convention under test: ProjectDomainType's int-converted key
        // needs a manual value on the in-memory provider (same workaround AuditStampingTests uses),
        // otherwise SaveChanges fails on the lookup row — never touch Project's own Id here.
        var domainType = context.ChangeTracker.Entries<ProjectDomainType>().Single();
        var key = domainType.Metadata.FindPrimaryKey()!.Properties[0].Name;
        domainType.Property(key).CurrentValue = new ProjectDomainTypeId(2000);

        await context.SaveChangesAsync(ct);

        var stampedId = (ProjectId)context.Entry(project).Property("Id").CurrentValue!;

        Assert.NotEqual(Guid.Empty, stampedId.Value);
    }

    [Fact]
    public void Should_LeaveIntIdentityLookupKeyUntouched_When_ModelIsBuilt()
    {
        // ProjectDomainType's PK providerType is int (through its converter), not Guid — the
        // convention's own type filter (`providerType != typeof(Guid)`) must skip it, so it keeps
        // whatever value-generation behavior its own EntityTypeConfiguration set up rather than
        // getting a UuidV7ValueGenerator.
        var context = _serviceProvider.GetRequiredService<DatabaseContext>();
        var entityType = context.Model.FindEntityType(typeof(ProjectDomainType))!;
        var keyProperty = entityType.FindPrimaryKey()!.Properties.Single();

        var factory = keyProperty.GetValueGeneratorFactory();
        var generator = factory?.Invoke(keyProperty, entityType);

        Assert.False(generator is UserOrg.Shared.Persistance.UuidV7ValueGenerator);
    }

    [Fact]
    public void Should_LeaveCompositeKeyJoinEntityUntouched_When_ModelIsBuilt()
    {
        // AssignedUserToProject has a composite key (ProjectId, UserId) — the convention only acts
        // on single-property keys (`key.Properties.Count != 1` short-circuits), so neither key
        // property should carry a UuidV7ValueGenerator even though both are individually Guid-typed.
        var context = _serviceProvider.GetRequiredService<DatabaseContext>();
        var entityType = context.Model.FindEntityType(typeof(Domain.Project.AssignedUserToProject))!;
        var keyProperties = entityType.FindPrimaryKey()!.Properties;

        Assert.Equal(2, keyProperties.Count);
        foreach (var keyProperty in keyProperties)
        {
            var factory = keyProperty.GetValueGeneratorFactory();
            var generator = factory?.Invoke(keyProperty, entityType);
            Assert.False(generator is UserOrg.Shared.Persistance.UuidV7ValueGenerator);
        }
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
