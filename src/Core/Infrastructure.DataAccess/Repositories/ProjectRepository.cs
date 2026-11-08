using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Extensions.Extensions;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Domain.Contracts;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories.AbstractTypes;

namespace UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

public class ProjectRepository(DatabaseContext context) : GenericRepository<Project, ProjectId>(context), IProjectRepository
{
    public IQueryable<Project> Query() => DbSet.Where(p => !p.IsDeleted).AsNoTracking();
    public override void Delete(Project entityToDelete)
    {
        var project = DbSet
            .Include(p => p.AssignedProjectResources)
            .Include(p => p.ProjectTags)
            .Include(p => p.AssignedUsers)
            .Include(p => p.UserPermissions)
            .AsSplitQuery()

            .First(f => f.Id == entityToDelete.Id);

        Context.AssignedProjectResourceToProjects.RemoveRange(project.AssignedProjectResources);
        Context.AssignedUserToProjects.RemoveRange(project.AssignedUsers);
        Context.UserPermissions.RemoveRange(project.UserPermissions);

        DbSet.Remove(project);
    }

    public override void Delete(ProjectId id)
    {
        var project = DbSet
            .Include(p => p.AssignedProjectResources)
            .Include(p => p.ProjectTags)
            .Include(p => p.AssignedUsers)
            .Include(p => p.UserPermissions)
            .AsSplitQuery()

            .First(f => f.Id == id);

        Context.AssignedProjectResourceToProjects.RemoveRange(project.AssignedProjectResources);
        Context.AssignedUserToProjects.RemoveRange(project.AssignedUsers);
        Context.UserPermissions.RemoveRange(project.UserPermissions);

        DbSet.Remove(project);
    }

    public override async Task DeleteAsync(ProjectId id, CancellationToken cancelationToken)
    {
        var project = await DbSet
            .Include(p => p.AssignedProjectResources)
            .Include(p => p.AssignedUsers)
            .Include(p => p.UserPermissions)
            .AsSplitQuery()

            .FirstAsync(f => f.Id == id, cancelationToken)
            ;

        Context.AssignedProjectResourceToProjects.RemoveRange(project.AssignedProjectResources);
        Context.AssignedUserToProjects.RemoveRange(project.AssignedUsers);
        Context.UserPermissions.RemoveRange(project.UserPermissions);

        DbSet.Remove(project);
    }

    /// <summary>
    /// Asynchronously retrieves information about a project resource based on the specified project short name and
    /// resource name.
    /// </summary>
    /// <remarks>Only active (non-deleted) projects and resources are considered when searching for the
    /// specified resource.</remarks>
    /// <param name="projectShortName">The short name of the project to search for. Cannot be null.</param>
    /// <param name="resourceName">The name of the resource to retrieve within the specified project. Cannot be null.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the project resource information if
    /// found; otherwise, null.</returns>
    public async Task<ProjectResourceInfo?> GetByShortNameAndResourceNameAsync(ProjectShortName projectShortName, string resourceName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectShortName);
        ArgumentNullException.ThrowIfNull(resourceName);

        var dbResult = await DbSet.AsNoTracking()
            .TagWith("GetProjectByShortNameAndResourceNameAsync")
            .Where(p => !p.IsDeleted && p.ShortName == projectShortName)
            .SelectMany(p => p.AssignedProjectResources
                .Where(apr => !apr.IsDeleted
                              && apr.ProjectResource != null
                              && !apr.ProjectResource.IsDeleted
                              && apr.ProjectResource!.Name == resourceName),
                (p, apr) => new
                {
                    ProjectId = p.Id,
                    ProjectName = p.Name,
                    ProjectShortName = p.ShortName,
                    ProjectDescription = p.Description,
                    ProjectStart = p.StartDate,
                    ProjectEnd = p.EndDate,
                    ResourceId = apr.ProjectResource!.Id,
                    ResourceName = apr.ProjectResource.Name,
                    ResourceDescription = apr.ProjectResource.Description,
                    ResourceStart = apr.ProjectResource.StartDate,
                    ResourceEnd = apr.ProjectResource.EndDate,
                    Locations = apr.ProjectResource.AssignedLocations
                           .Where(al => !al.IsDeleted
                               && al.Location != null
                               && !al.Location.IsDeleted
                               && al.Location.LocationType != null
                              && al.AssignedLocationsValues.Any(a =>
                                   a.Name != null
                                   && a.Value != null
                                   && EF.Functions.Collate(a.Name, "SQL_Latin1_General_CP1_CI_AS") == "HEAPPE_IDENTIFIER"))
                           .Select(ss => new
                           {
                               LocationId = ss.Location!.Id,
                               LocationName = ss.Location.Name,
                               LocationEndpoint = ss.Location.Endpoint,
                               LocationTypeId = ss.Location.LocationType!.Id,
                               AggregationName = ss.AggregationName,
                               Values = ss.AssignedLocationsValues
                                   .Where(w => w.Name != null && w.Value != null)
                                   .Select(v => new KeyValuePair<string, string>(v.Name!, v.Value!))
                                   .ToList()
                           })
                           .ToList()
                })
            .FirstOrDefaultAsync(ct)
            ;

        if (dbResult == null)
        {
            return null;
        }

        var projectResourceInfo = new ProjectResourceInfo(
          Id: dbResult.ProjectId,
          Name: dbResult.ProjectName!,
          ShortName: dbResult.ProjectShortName,
          Description: dbResult.ProjectDescription,
          StartDate: dbResult.ProjectStart,
          EndDate: dbResult.ProjectEnd,
          Resources: new[]
          {
                new ResourceInfo(
                    Id: dbResult.ResourceId,
                    Name: dbResult.ResourceName!,
                    Description: dbResult.ResourceDescription,
                    StartDate: dbResult.ResourceStart,
                    EndDate: dbResult.ResourceEnd,
                    LocationAllocations: dbResult.Locations
                        .Select(s => ToLocationAllocation(s.LocationId, s.LocationName, s.LocationEndpoint, s.LocationTypeId.AsLocationType(), s.AggregationName, s.Values))
                        .OfType<LocationAllocationInfo>()
                        .ToList()
                )
            }
        );
        return projectResourceInfo;
    }

    public async Task<Project> GetByShortNameAsync(ProjectShortName projectShortName, CancellationToken ct) => await DbSet.Where(p => p.ShortName == projectShortName && !p.IsDeleted)
            .Include(p => p.AssignedUsers).ThenInclude(c => c.User)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ProjectTags)
            .Include(p => p.ProjectDomainType)
            .Include(p => p.AssignedProjectResources)
                .ThenInclude(p => p.ProjectResource)
        .Include(x => x.UserPermissions).ThenInclude(x => x.User)
        .AsSplitQuery()
            .Where(w => !w.IsDeleted)
            .FirstOrDefaultAsync(ct)
            ;

    public async Task<Project?> GetByShortNameWithDetailsAsync(ProjectShortName projectShortName, CancellationToken ct) => await DbSet.Where(p => p.ShortName == projectShortName && !p.IsDeleted)
            .Include(p => p.ProjectTags)
            .Include(p => p.ProjectDomainType)
            .Include(p => p.AssignedProjectResources)
                .ThenInclude(p => p.ProjectResource)
                .ThenInclude(x => x.AssignedLocations)
                .ThenInclude(x => x.AssignedLocationsValues)

        .Include(p => p.AssignedProjectResources)
                .ThenInclude(p => p.ProjectResource)
                .ThenInclude(x => x.AssignedLocations)
                .ThenInclude(x => x.AllocationUnitType)

        .Include(p => p.AssignedProjectResources)
                .ThenInclude(p => p.ProjectResource)
                .ThenInclude(x => x.AssignedLocations)
                .ThenInclude(x => x.Location).ThenInclude(l => l.LocationType)

        .AsSplitQuery()
            .Where(w => !w.IsDeleted)
            .FirstOrDefaultAsync(ct)
            ;

    public async Task<Project?> GetProjectByShortNameWithCreatedUsersAsync(ProjectShortName projectShortName, CancellationToken ct) => await DbSet.Where(p => p.ShortName == projectShortName && !p.IsDeleted)
            .Include(p => p.ProjectDomainType)
            .Include(p => p.CreatedByUser)
            .Include(p => p.AssignedUsers)
                .ThenInclude(x => x.User)
            .Include(p => p.UserPermissions)
            .Include(p => p.AssignedProjectResources)
                .ThenInclude(p => p.ProjectResource)
                .ThenInclude(x => x.AssignedLocations)
                .ThenInclude(x => x.Location).ThenInclude(l => l.LocationType)
            .Where(w => !w.IsDeleted)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct)
            ;

    public async Task<List<Project>> GetProjectsContainsUserAsync(ProjectShortName? projectShortName, string idpUserId, CancellationToken ct)
    {
        var query = DbSet
            .Include(p => p.AssignedUsers.Where(au => !au.IsDeleted))
                .ThenInclude(x => x.User)
            .Include(p => p.CreatedByUser)
            .Include(p => p.ProjectDomainType)
            .Include(p => p.AssignedProjectResources)
                .ThenInclude(i => i.ProjectResource)
            .Include(p => p.ProjectTags)
            .Include(p => p.UserPermissions.Where(up => !up.IsDeleted))
            .AsSplitQuery()
            .Where(x => (x.CreatedByUser.IdentityProviderUserId == idpUserId || x.AssignedUsers.Any(a => !a.IsDeleted && a.User.IdentityProviderUserId == idpUserId)) && !x.IsDeleted)
            .AsQueryable();

        if (projectShortName.HasValue)
        {
            query = query.Where(x => EF.Functions.Like(x.ShortName, projectShortName))
                .AsQueryable();
        }

        return await query.ToListAsync(ct);
    }

    public async Task<IList<Project>> GetProjectsForProvider(Guid providerId, CancellationToken GetProjectsForProvider) => await DbSet
            .Include(p => p.AssignedProjectResources)
              .ThenInclude(x => x.ProjectResource)
              .ThenInclude(x => x.AssignedLocations)
              .ThenInclude(x => x.Location)
              .ThenInclude(x => x.Provider)
            .Include(p => p.AssignedUsers)
              .ThenInclude(u => u.User)
              .AsSplitQuery()
            .Where(x => x.AssignedProjectResources
              .Any(r => r.ProjectResource.AssignedLocations
                .Any(l => l.Location.Provider.Id == providerId)) && !x.IsDeleted)
            .ToListAsync(GetProjectsForProvider);

    public async Task<List<ProjectExtendedProjection>> GetProjectsContainsUserExtendedAsync(
        ProjectShortName? projectShortName, string idpUserId, CancellationToken ct)
    {
        // Projection-based query: only requested columns are emitted in SQL (no SELECT *).
        // No Include/ThenInclude, no entity tracking, no over-fetching of Provider/Country/
        // UserPermissions/AssignedUsers etc. that are never read by the callers.
        //
        // Two-phase approach:
        //   Phase 1 (SQL): project to anonymous type — EF Core translates nested .Select()
        //                  as correlated sub-queries, avoiding cartesian explosion.
        //   Phase 2 (C#) : materialize to ProjectExtendedProjection / LocationDetailModel,
        //                  applying TrimEnd('/') on URL values (not SQL-translatable).

        var baseQuery = DbSet
            .Where(x => !x.IsDeleted
                && (x.CreatedByUser.IdentityProviderUserId == idpUserId
                    || x.AssignedUsers.Any(a => !a.IsDeleted && a.User.IdentityProviderUserId == idpUserId)));

        if (projectShortName.HasValue)
            baseQuery = baseQuery.Where(x => EF.Functions.Like(x.ShortName, projectShortName));

        // Phase 1: SQL projection — anonymous types only, EF Core translates this cleanly
        var rawData = await baseQuery
            .Select(p => new
            {
                p.Id,
                p.ShortName,
                Resources = p.AssignedProjectResources
                    .Where(apr => !apr.IsDeleted && !apr.ProjectResource.IsDeleted)
                    .Select(apr => new
                    {
                        apr.ProjectResource.Name,
                        apr.ProjectResource.Description,
                        apr.ProjectResource.StartDate,
                        apr.ProjectResource.EndDate,
                        Locations = apr.ProjectResource.AssignedLocations
                            .Where(al => !al.IsDeleted)
                            .Select(al => new
                            {
                                LocationName = al.Location.Name,
                                LocationDescription = al.Location.Description,
                                LocationEndpoint = al.Location.Endpoint,
                                al.Location.IsPrivate,
                                LocationTypeName = al.Location.LocationType.Name,
                                al.AllocationUnitCount,
                                AllocationUnitTypeName = al.AllocationUnitType.Name,
                                Values = al.AssignedLocationsValues
                                    .Select(v => new { v.Name, v.Value })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToListAsync(ct);

        // Phase 2: C# materialization into domain projection records
        return rawData
            .Select(p => new ProjectExtendedProjection(
                p.Id,
                p.ShortName,
                p.Resources
                    .Select(r => new ProjectResourceDetailModel(
                        r.Name ?? string.Empty,
                        r.Description ?? string.Empty,
                        r.StartDate,
                        r.EndDate,
                        r.Locations
                            .Select(l => new LocationDetailModel(
                                l.LocationName ?? string.Empty,
                                l.LocationDescription ?? string.Empty,
                                l.LocationEndpoint ?? string.Empty,
                                l.IsPrivate,
                                l.LocationTypeName ?? string.Empty,
                                l.AllocationUnitCount,
                                l.AllocationUnitTypeName ?? string.Empty,
                                l.Values.Select(v => new KeyValuePair<string, string>(
                                    v.Name ?? string.Empty,
                                    (v.Value ?? string.Empty).TrimEnd('/')))
                            ))
                            .ToList()
                    ))
                    .ToList()
            ))
            .ToList();
    }

    /// <summary>
    /// Retrieves projects filtered by various criteria, with optional provider scoping.
    /// </summary>
    /// <param name="shortName">Optional short name filter (case-insensitive LIKE match).</param>
    /// <param name="contactEmail">Optional contact email filter (substring match).</param>
    /// <param name="domainId">Optional domain ID filter.</param>
    /// <param name="startDate">Optional start date filter (inclusive, projects must start on or after this date).</param>
    /// <param name="endDate">Optional end date filter (inclusive, projects must end on or before this date).</param>
    /// <param name="providerId">Optional provider ID for scoping results to a provider's assigned resources.
    /// When <c>null</c>, no provider filter is applied (platform admin sees all projects).
    /// When set to a provider GUID, the result is the union of projects whose resource-assignment chain (via AssignedProjectResources → ProjectResource → AssignedLocations → Location) resolves to that provider and the caller's own projects (see <paramref name="callerIdpUserId"/>).
    /// Soft-deleted link rows still count, so a provider keeps seeing projects whose allocations expired, were terminated or archived upstream.
    /// Soft-deleted projects are excluded regardless of the scope.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="callerIdpUserId">The calling ProviderAdmin's identity-provider user id. When
    /// <paramref name="providerId"/> is set, a project the caller is assigned to or created is included even
    /// outside the provider's resource chain. Ignored when <paramref name="providerId"/> is <c>null</c>.</param>
    /// <returns>A list of projects matching all supplied filters.</returns>
    public async Task<IList<Project>> GetAllBySpecificationsAsync(string? shortName, string? contactEmail, int? domainId, DateTime? startDate, DateTime? endDate, Guid? providerId, CancellationToken ct, string? callerIdpUserId = null)
    {
        var projects = DbSet
            .AsSplitQuery()
            .Include(i => i.ProjectDomainType)
            .Include(i => i.ProjectTags)
            .Include(i => i.AssignedProjectResources)
                .ThenInclude(x => x.ProjectResource)
            .AsQueryable();

        var filters = new List<Expression<Func<Project, bool>>>();

        Expression<Func<Project, bool>> userPredicate = (startDate, endDate) switch
        {
            (null, DateTime) => w => !w.IsDeleted && w.EndDate <= endDate,
            (DateTime, null) => w => !w.IsDeleted && w.StartDate >= startDate,
            (DateTime, DateTime) => w => !w.IsDeleted && w.StartDate >= startDate && w.EndDate <= endDate,
            _ => w => !w.IsDeleted
        };
        filters.Add(userPredicate);

        if (!string.IsNullOrEmpty(shortName))
        {
            filters.Add(w => EF.Functions.Like(w.ShortName, shortName));
        }

        if (!string.IsNullOrEmpty(contactEmail))
        {
            filters.Add(w => w.Email.Contains(contactEmail));
        }

        if (domainId is not null)
        {
            filters.Add(w => w.ProjectDomainType.Id == domainId);
        }

        // Neither hop of the chain is soft-delete filtered: the Waldur sync writes IsDeleted on a link when the
        // allocation is terminated or its offering archived, which is an expired binding, not a removed one.
        // Filtering it would hide a provider's own projects the moment their allocations ran out. The project
        // itself is still filtered above - a deleted project is gone for everybody.
        if (providerId is Guid scopedProviderId)
        {
            if (string.IsNullOrEmpty(callerIdpUserId))
            {
                filters.Add(w => w.AssignedProjectResources
                    .Any(r => r.ProjectResource.AssignedLocations
                        .Any(l => l.Location.ProviderId == scopedProviderId)));
            }
            else
            {
                filters.Add(w => w.AssignedProjectResources
                        .Any(r => r.ProjectResource.AssignedLocations
                            .Any(l => l.Location.ProviderId == scopedProviderId))
                    || w.CreatedByUser.IdentityProviderUserId == callerIdpUserId
                    || w.AssignedUsers.Any(a => !a.IsDeleted && a.User.IdentityProviderUserId == callerIdpUserId));
            }
        }

        filters.ForEach(f => projects = projects.Where(f));
        return await projects.ToListAsync(ct);
    }

    public async Task<IList<IProjectHEAppELinks>> GetHeappeLinks(ProjectShortName projectShortName, CancellationToken ct)
    {
        var queryRes = DbSet.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.AssignedProjectResources.Where(w => !w.IsDeleted && !w.ProjectResource.IsDeleted))
                .ThenInclude(x => x.ProjectResource)
                    .ThenInclude(x => x.AssignedLocations.Where(w => !w.IsDeleted))
                        .ThenInclude(x => x.AssignedLocationsValues)
            .Include(x => x.AssignedProjectResources.Where(w => !w.IsDeleted && !w.ProjectResource.IsDeleted))
                .ThenInclude(x => x.ProjectResource)
                    .ThenInclude(x => x.AssignedLocations.Where(w => !w.IsDeleted))
                        .ThenInclude(x => x.Location)
                            .ThenInclude(x => x.Provider)
            .Where(x => x.ShortName == projectShortName && !x.IsDeleted);

        var dataRes = await queryRes.ToListAsync(ct);
        var project = await DbSet
            .AsNoTracking()
            .Where(x => x.ShortName == projectShortName && !x.IsDeleted)
            .FirstOrDefaultAsync(ct)
            ;

        List<int> validLocationTypeIds = [(int)ELocationType.HPC, (int)ELocationType.Quantum];

        var rtn = dataRes.Where(x => x.AssignedProjectResources.Any())
            .SelectMany(q => q.AssignedProjectResources)
            .SelectMany(x => x.ProjectResource.AssignedLocations.Where(al => validLocationTypeIds.Contains(al.Location.LocationTypeId)))
            .Where(x => x.AssignedLocationsValues.Any(y => y.Name.Equals("HEAPPE_URL", StringComparison.OrdinalIgnoreCase) || y.Name.Equals("HEAPPE_IDENTIFIER", StringComparison.OrdinalIgnoreCase)))
            .Select(x => new ProjectHEAppELinks
            {
                HEAppEApi = x.AssignedLocationsValues.FirstOrDefault(y => y.Name.Equals("HEAPPE_URL", StringComparison.OrdinalIgnoreCase))?.Value?.ToNormalizeUrl(),
                HEAppEApiVersion = (x.AssignedLocationsValues.FirstOrDefault(y => y.Name.Equals("HEAPPE_VERSION", StringComparison.OrdinalIgnoreCase))?.Value).TryExtractVersion(out var version) ? version! : new Version(0, 0, 0),
                // A link without a valid identifier stays in the list: the caller skips it with a warning (ADR heappe-via-nexus-proxy).
                HEAppEApiIdentifier = HeappeIdentifier.TryParse(x.AssignedLocationsValues.FirstOrDefault(y => y.Name.Equals("HEAPPE_IDENTIFIER", StringComparison.OrdinalIgnoreCase))?.Value, out var identifier) ? identifier : null,
                ResourceName = x.ProjectResource.Name,
                LocationName = x.Location.Name,
                LocationId = x.Location.Id.ToString(),
                ProviderName = x.Location.Provider.Name,
                ProjectStartDate = project.StartDate,
                ProjectEndDate = project.EndDate,
                ResourceStartDate = x.ProjectResource.StartDate,
                ResourceEndDate = x.ProjectResource.EndDate,
            })
            .ToList<IProjectHEAppELinks>();

        return rtn;
    }

    /// <summary>The version of the HEAppE instance with <paramref name="heappeIdentifier"/> as stored with its locations, or <c>null</c> when no location carries it.</summary>
    public async Task<HeappeInstance?> GetHeappeVerison(HeappeIdentifier heappeIdentifier, CancellationToken ct)
    {
        var queryRes = DbSet.AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.AssignedProjectResources.Where(w => !w.IsDeleted && !w.ProjectResource.IsDeleted))
                .ThenInclude(x => x.ProjectResource)
                    .ThenInclude(x => x.AssignedLocations.Where(w => !w.IsDeleted))
                        .ThenInclude(x => x.AssignedLocationsValues)
            .Include(x => x.AssignedProjectResources.Where(w => !w.IsDeleted && !w.ProjectResource.IsDeleted))
                .ThenInclude(x => x.ProjectResource)
                .ThenInclude(x => x.AssignedLocations.Where(w => !w.IsDeleted))
                .ThenInclude(x => x.Location)
                    .ThenInclude(x => x.Provider)
            .Where(x => !x.IsDeleted);

        var dataRes = await queryRes.ToListAsync(ct);
        var rtn = dataRes
            .SelectMany(q => q.AssignedProjectResources)
            .SelectMany(x => x.ProjectResource.AssignedLocations)
            .Select(x => x.AssignedLocationsValues.Select(v => new KeyValuePair<string, string>(v.Name, v.Value)).ToList().ToHeappeInstance())
            .OfType<HeappeInstance>()
            .Where(x => x.Identifier == heappeIdentifier)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        return rtn;
    }

    // A location whose values carry no valid HEAPPE_IDENTIFIER cannot be routed through NexusApi and is left out.
    private static LocationAllocationInfo? ToLocationAllocation(
        Guid locationId,
        string? locationName,
        string? loginEndpoint,
        ELocationType locationType,
        string? aggregationName,
        IList<KeyValuePair<string, string>> values) =>
        values.ToHeappeInstance() is { } instance
            ? new LocationAllocationInfo(locationId, locationName!, loginEndpoint!, locationType, aggregationName, instance)
            : null;

    public async Task<IList<ProjectAllocationInfo>> GetProjectResourceAllocationInfoByUserIdAndHeappeIdentifierAsync(
        Guid userId,
        string heappeIdentifier,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userId);
        ArgumentNullException.ThrowIfNull(heappeIdentifier);

        var actualDate = DateTime.UtcNow;
        var dbResult = await DbSet.AsNoTracking()
           .AsSplitQuery()
           .Where(w => !w.IsDeleted
               && w.AssignedUsers.Any(a => a.User != null && a.User.Id == userId)
               && w.StartDate <= actualDate
               && actualDate <= w.EndDate)
           .Select(s => new
           {
               ProjectId = s.Id,
               ProjectShortName = s.ShortName,
               Resources = s.AssignedProjectResources
                   .Where(w => !w.IsDeleted
                       && w.ProjectResource != null
                       && !w.ProjectResource.IsDeleted
                       && w.ProjectResource.StartDate <= actualDate
                       && actualDate <= w.ProjectResource.EndDate)
                   .Select(apr => new
                   {
                       ResourceId = apr.ProjectResource!.Id,
                       ResourceName = apr.ProjectResource!.Name,
                       Locations = apr.ProjectResource.AssignedLocations
                           .Where(al => !al.IsDeleted
                               && al.Location != null
                               && !al.Location.IsDeleted
                               && al.Location.LocationType != null
                               && (al.Location.LocationType.Id == 1 || al.Location.LocationType.Id == 7)  // HPC or Quantum
                               && al.AssignedLocationsValues.Any(a =>
                                   a.Name != null
                                   && a.Value != null
                                   && EF.Functions.Collate(a.Name, "SQL_Latin1_General_CP1_CI_AS") == "HEAPPE_IDENTIFIER"
                                   && a.Value == heappeIdentifier))
                           .Select(ss => new
                           {
                               LocationId = ss.Location!.Id,
                               LocationName = ss.Location.Name,
                               LocationEndpoint = ss.Location.Endpoint,
                               LocationTypeId = ss.Location.LocationType!.Id,
                               AggregationName = ss.AggregationName,
                               Values = ss.AssignedLocationsValues
                                   .Where(w => w.Name != null && w.Value != null)
                                   .Select(v => new KeyValuePair<string, string>(v.Name!, v.Value!))
                                   .ToList()
                           })
                           .ToList()
                   })
                   .ToList()
           })
           .ToListAsync(ct);

        var result = dbResult
            .Select(project => new ProjectAllocationInfo(
                Id: project.ProjectId,
                ShortName: project.ProjectShortName,
                ResourceAllocations: project.Resources
                    .Where(resource => resource.Locations.Count > 0)
                    .GroupBy(resource => resource!)
                    .Select(s => new ResourceAllocationInfo(
                        s.Key.ResourceId,
                        s.Key.ResourceName!,
                        s.SelectMany(se => se.Locations)
                        .Select(location => ToLocationAllocation(location.LocationId, location.LocationName, location.LocationEndpoint, (ELocationType)location.LocationTypeId, location.AggregationName, location.Values))
                        .OfType<LocationAllocationInfo>()
                        .ToList()

                    )).ToList()
            ))
            .Where(project => project.ResourceAllocations.Count > 0)
            .ToList();

        return result;

    }

    public async Task<IList<WeekCount>> CreatedFrom(DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var weeks = WeekPart.GetWeeksFromStartDate(fromDate, toDate);
        var limitDateFrom = fromDate.ToDateTime(TimeOnly.MinValue);
        var limitDateTo = toDate.ToDateTime(TimeOnly.MaxValue);
        var query = await DbSet
            .Where(x => !x.IsDeleted && x.CreatedAt >= limitDateFrom && x.CreatedAt <= limitDateTo)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new { Idx = WeekPart.GetWeekPartForDate(weeks, x.CreatedAt), Data = x })
            .ToListAsync(ct)
            ;

        return query.GroupBy(g => g.Idx)
            .Select(x => new WeekCount(DateOnly.FromDateTime(x.Key.StartDate), DateOnly.FromDateTime(x.Key.EndDate), x.Count()))
            .ToList();
    }

    public async Task<int> TotalCount(CancellationToken ct) => await DbSet.CountAsync(x => !x.IsDeleted, ct)
            ;

    public async Task<IList<(string ShortName, Guid Id, string ResourceName)>> GetAllWithResources(CancellationToken ct)
    {
        var today = DateTime.UtcNow;

        var data = await DbSet
            .Where(x => !x.IsDeleted && x.StartDate <= today && x.EndDate > today)
            .SelectMany(x => x.AssignedProjectResources
                .Where(r => !r.IsDeleted && r.ProjectResource != null && !r.ProjectResource.IsDeleted && r.ProjectResource.StartDate <= today && r.ProjectResource.EndDate > today)
                .Select(aa => new { ShortName = x.ShortName, ProjectId = x.Id.Value, ResourceName = aa.ProjectResource!.Name }))
            .ToListAsync(ct);

        return data.Select(d => (ShortName: d.ShortName.Value, Id: d.ProjectId, d.ResourceName))?.ToList() ?? [];
    }
}