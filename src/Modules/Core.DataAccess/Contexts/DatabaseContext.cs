using System.Linq.Expressions;

using Core.DataAccess.Conventions;
using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Persistance;

using WellKnownRoles = Core.Domain.User.WellKnownRoles;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Database context for UserOrganization domain.
/// Internal implementation — infrastructure detail of Core.DataAccess.
/// Implements public contracts via partial classes (see Contexts/ folder):
/// - DatabaseContext.UnitOfWork.cs (IUnitOfWork)
/// - DatabaseContext.Repository.cs (IRepository{Project, ProjectId})
/// - DatabaseContext.DataQuery.cs (IDataQuery{T})
/// </summary>
internal sealed partial class DatabaseContext : DbContext
{
    private readonly DbContextOptions<DatabaseContext> _options;
    private readonly DomainEventChannel _domainEventChannel;
    private readonly IRoleService _roleService;
    private readonly IOptions<WellKnownRoles> _wellKnownRoleOptions;
    private readonly ILogger<DatabaseContext> _logger;

    // Aggregate roots added through a repository AddAsync overload in the current unit of work (T-055-03).
    // SaveChangesAsync on the IUnitOfWork path treats any AggregateRoot in EntityState.Added that is NOT in
    // this set as a cascaded insert nobody asked for (e.g. a detached navigation dragged along by
    // ChangeTracker.DetectChanges) and throws instead of silently persisting it. Reference equality —
    // identity, not value equality — is exactly what distinguishes "this instance went through AddAsync"
    // from "an equal-looking instance got attached some other way". Cleared after every save.
    private readonly HashSet<AggregateRoot> _registeredAggregateRoots =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Marks <paramref name="aggregate"/> as added through a repository in the current unit of work, so the
    /// detached-aggregate guard in <c>DatabaseContext.UnitOfWork.cs</c> does not reject it. Called by every
    /// <c>IRepository&lt;T,K&gt;.AddAsync</c> overload after the aggregate is queued for insert.
    /// </summary>
    private void RegisterAddedAggregateRoot(AggregateRoot aggregate) =>
        _registeredAggregateRoots.Add(aggregate);

    public DbSet<Domain.Project.Project> Projects => Set<Domain.Project.Project>();
    public DbSet<Domain.User.User> Users => Set<Domain.User.User>();
    public DbSet<Domain.Provider.Provider> Providers => Set<Domain.Provider.Provider>();
    public DbSet<Domain.Provider.ProjectResource> ProjectResources => Set<Domain.Provider.ProjectResource>();
    public DbSet<Domain.Request.Request> Requests => Set<Domain.Request.Request>();
    public DbSet<Domain.OnboardingRequest.OnboardingRequest> OnboardingRequests => Set<Domain.OnboardingRequest.OnboardingRequest>();

    public DatabaseContext(DbContextOptions<DatabaseContext> options, DomainEventChannel domainEventChannel, IRoleService roleService, IOptions<WellKnownRoles> wellKnownRoleOptions, ILogger<DatabaseContext> logger)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(domainEventChannel);
        ArgumentNullException.ThrowIfNull(roleService);
        ArgumentNullException.ThrowIfNull(wellKnownRoleOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _domainEventChannel = domainEventChannel;
        _roleService = roleService;
        _wellKnownRoleOptions = wellKnownRoleOptions;
        _logger = logger;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(DatabaseContext).Assembly,
            t => t.Namespace == "Core.DataAccess.Configurations");

        // Reproduce the legacy audit-column schema (CreatedAt/ModifiedAt/IsDeleted) across all
        // soft-deletable domain rows — runs after the per-entity configs so it is authoritative.
        modelBuilder.ApplyAuditableConventions();

        // Guid PKs are code-generated UUID v7 — drop any DB-side NewId() default.
        modelBuilder.ApplyCodeGeneratedGuidKeys();

        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    private static readonly HashSet<Type> OwnFilterTypes =
    [
        typeof(Domain.User.UserPermission),
        typeof(Domain.Provider.Provider),
        typeof(Domain.Project.AssignedProjectResourceToProject),
        typeof(Domain.Project.AssignedUserToProject),
    ];

    /// <summary>
    /// Excludes soft-deleted rows by default for every <see cref="ISoftDeletableEntity"/> —
    /// the DbContext-wide default. <see cref="Domain.User.UserPermission"/> is skipped because it
    /// carries a deliberate, non-soft-delete global filter (<c>ProjectId == null</c>); its callers
    /// apply <c>!IsDeleted</c> explicitly. <see cref="Domain.Provider.Provider"/>,
    /// <see cref="Domain.Project.AssignedProjectResourceToProject"/> and
    /// <see cref="Domain.Project.AssignedUserToProject"/> are skipped because they define
    /// their own named soft-delete filter that a repository can ignore selectively.
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (OwnFilterTypes.Contains(entityType.ClrType))
                continue;

            if (!typeof(ISoftDeletableEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Not(
                Expression.Property(parameter, nameof(ISoftDeletableEntity.IsDeleted)));
            entityType.SetQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}