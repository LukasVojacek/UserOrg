using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base.Sync;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

namespace Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

internal static class WaldurProjectResourceContextExtensions
{
    private const string _UnspecifiedAllocationUnitTypeName = "unspecified";

    extension(IUserOrgDbContext context)
    {
        internal async Task<ProjectDomainType> EnsureProjectDomainTypeAsync(
            ILogger logger,
            string integrationName,
            string name,
            CancellationToken ct)
        {
            var dbDomainType = await context.ProjectDomainTypesQuery
                .FirstOrDefaultAsync(pdt => pdt.Name == name, ct);
            if (dbDomainType is not null)
            {
                return dbDomainType;
            }

            dbDomainType = new ProjectDomainType { Name = name };
            logger.CreatingProjectDomainType(integrationName, dbDomainType.Name);
            context.Add(dbDomainType);
            return dbDomainType;
        }

        internal async Task<Project> UpsertProjectAsync(
            ILogger logger,
            string integrationName,
            Project project,
            User createdByUser,
            CancellationToken ct)
        {
            var dbProject = await context.ProjectsQuery.FirstOrDefaultAsync(p => p.ExternalId == project.ExternalId && p.ServiceName == project.ServiceName, ct);

            if (dbProject is null)
            {
                var dbDomainType = await context.EnsureProjectDomainTypeAsync(logger, integrationName, project.ProjectDomainType!.Name, ct);
                var projectToInsert = CreateProjectForInsert(project, dbDomainType, createdByUser);
                context.Add(projectToInsert);
                return projectToInsert;
            }

            dbProject.SyncFrom(project);
            return dbProject;
        }

        internal async Task<Result<Location, Error>> RequireLocationByEndpointAsync(
            ILogger logger,
            string integrationName,
            string? endpoint,
            string? serviceName,
            string projectSlug,
            CancellationToken ct)
        {
            var endpointAndServiceMatches = await context.LocationsQuery
                .Include(x => x.Provider)
                .Where(l => l.Endpoint == endpoint && l.ServiceName == serviceName)
                .ToListAsync(ct);

            if (endpointAndServiceMatches.Count > 1)
            {
                logger.MultipleLocationsFoundForEndpointAndServiceDebug(integrationName, endpoint, serviceName);
                logger.LogWarning("Multiple locations found for endpoint {Endpoint} and service {ServiceName}.", endpoint, serviceName);
                return Result<Location, Error>.Failure(
                    Errors.EnsureProjectFailed(projectSlug, $"Ambiguous location for endpoint: {endpoint}"));
            }

            if (endpointAndServiceMatches.Count == 1)
            {
                return Result<Location, Error>.Success(endpointAndServiceMatches[0]);
            }

            var endpointMatches = await context.LocationsQuery
                .Include(x => x.Provider)
                .Where(l => l.Endpoint == endpoint)
                .ToListAsync(ct);

            if (endpointMatches.Count > 1)
            {
                logger.MultipleLocationsFoundForEndpointDebug(integrationName, endpoint);
                logger.LogWarning("Multiple locations found for endpoint {Endpoint}.", endpoint);
                return Result<Location, Error>.Failure(
                    Errors.EnsureProjectFailed(projectSlug, $"Ambiguous location for endpoint: {endpoint}"));
            }

            if (endpointMatches.Count == 1)
            {
                return Result<Location, Error>.Success(endpointMatches[0]);
            }

            if (endpointMatches.Count == 0)
            {
                logger.LocationNotFoundForEndpointDebug(integrationName, endpoint);
                logger.LocationNotFoundForEndpoint(integrationName, endpoint);
                return Result<Location, Error>.Failure(
                    Errors.EnsureProjectFailed(projectSlug, $"Location not found for endpoint: {endpoint}"));
            }

            logger.LocationNotFoundForEndpointDebug(integrationName, endpoint);
            logger.LocationNotFoundForEndpoint(integrationName, endpoint);
            return Result<Location, Error>.Failure(
                Errors.EnsureProjectFailed(projectSlug, $"Location not found for endpoint: {endpoint}"));
        }

        internal async Task<Result<Location, Error>> RequireLocationByEndpointAndProviderIdAsync(
            ILogger logger,
            string integrationName,
            string? endpoint,
            Guid providerId,
            string projectSlug,
            CancellationToken ct)
        {
            var dbLocation = await context.LocationsQuery
                .Include(x => x.Provider)
                .FirstOrDefaultAsync(l => l.Endpoint == endpoint && l.Provider!.Id == providerId, ct);
            if (dbLocation is null)
            {
                logger.LocationNotFoundForEndpointAndProviderDebug(integrationName, endpoint, providerId);
                logger.LocationNotFoundForEndpoint(integrationName, endpoint);
                return Result<Location, Error>.Failure(Errors.EnsureProjectFailed(projectSlug, $"Location not found for endpoint: {endpoint}"));
            }

            return Result<Location, Error>.Success(dbLocation);
        }

        internal async Task<Result<AllocationUnitType, Error>> RequireAllocationUnitTypeAsync(
            ILogger logger,
            string integrationName,
            string? name,
            string projectSlug,
            CancellationToken ct)
        {
            // W2: a resource with NO unit (null name) legitimately maps to "unspecified". But a
            // NON-NULL mapped unit type that is not seeded must FAIL LOUD (retriable) rather than
            // silently fall back to "unspecified": the fallback stores a different name than the
            // producer hashed (e.g. "core-hours"), so the cold rebuild reads "unspecified" back and
            // storms ProjectUpdated every restart. This mirrors RequireCountryAsync/RequireLocationTypeAsync.
            var lookupName = name ?? _UnspecifiedAllocationUnitTypeName;
            var dbAllocUnitType = await context.AllocationUnitTypesQuery.FirstOrDefaultAsync(a => a.Name == lookupName, ct);

            if (dbAllocUnitType is not null)
            {
                return Result<AllocationUnitType, Error>.Success(dbAllocUnitType);
            }

            logger.AllocationUnitTypeNotFoundDebug(integrationName, name);
            logger.AllocationUnitTypeNotFound(integrationName, name);
            return Result<AllocationUnitType, Error>.Failure(Errors.EnsureProjectFailed(projectSlug, $"AllocationUnitType not found for name: {lookupName}"));
        }

        internal async Task<ProjectResource> UpsertProjectResourceAsync(
            ProjectResource resource,
            CancellationToken ct)
        {
            var dbResource = await context.ProjectResourcesQuery
                .FirstOrDefaultAsync(r => r.Name == resource.Name && r.ServiceName == resource.ServiceName, ct);
            if (dbResource is null)
            {
                var resourceToInsert = new ProjectResource
                {
                    // ID will differ after persistence (source.Id is Guid.Empty); callers re-point to the returned instance.
                    Id = resource.Id,
                    Name = resource.Name,
                    Description = resource.Description,
                    PrimaryInvestigatorEmail = resource.PrimaryInvestigatorEmail,
                    StartDate = resource.StartDate,
                    EndDate = resource.EndDate,
                    CreatedAt = resource.CreatedAt,
                    ModifiedAt = resource.ModifiedAt,
                    IsDeleted = resource.IsDeleted,
                    Mode = resource.Mode,
                    ServiceName = resource.ServiceName,
                    ExternalId = resource.ExternalId
                };
                context.Add(resourceToInsert);
                return resourceToInsert;
            }

            dbResource.SyncFrom(resource);
            return dbResource;
        }

        internal async Task<AssignedLocation> UpsertAssignedLocationAsync(
            AssignedLocation assignedLocation,
            ProjectResource dbResource,
            AllocationUnitType dbAllocUnitType,
            CancellationToken ct)
        {
            var dbAssignedLocation = await context.AssignedLocationsQuery
                .Include(al => al.ProjectResource)
                .Include(al => al.Location)
                    .ThenInclude(location => location!.Provider)
                .Include(al => al.Location)
                    .ThenInclude(location => location!.LocationType)
                .Include(i => i.AssignedLocationsValues)
                .Include(i => i.AllocationUnitType)
                .FirstOrDefaultAsync(al => al.ExternalId == assignedLocation.ExternalId && al.ServiceName == assignedLocation.ServiceName, ct);
            if (dbAssignedLocation is null)
            {
                static void EnsureTrackedUnchanged<T>(IUserOrgDbContext dbContext, T entity) where T : class
                {
                    var entry = dbContext.Entry(entity);
                    if (entry.State == EntityState.Detached)
                    {
                        entry.State = EntityState.Unchanged;
                    }
                }

                if (assignedLocation.Location is not null)
                {
                    EnsureTrackedUnchanged(context, assignedLocation.Location);

                    if (assignedLocation.Location.Provider is not null)
                    {
                        EnsureTrackedUnchanged(context, assignedLocation.Location.Provider);
                    }

                    if (assignedLocation.Location.LocationType is not null)
                    {
                        EnsureTrackedUnchanged(context, assignedLocation.Location.LocationType);
                    }
                }

                EnsureTrackedUnchanged(context, dbResource);
                EnsureTrackedUnchanged(context, dbAllocUnitType);

                var assignedLocationToInsert = CreateAssignedLocationForInsert(assignedLocation, dbResource, dbAllocUnitType);
                context.Add(assignedLocationToInsert);

                return assignedLocationToInsert;
            }

            dbAssignedLocation.SyncFrom(assignedLocation, dbAllocUnitType,
                out var newAssignedLocationValues,
                out var removedAssignedLocationValues);

            //TODO should be moved into SyncFrom
            if (assignedLocation.Location is not null && dbAssignedLocation.LocationId != assignedLocation.Location.Id)
            {
                dbAssignedLocation.LocationId = assignedLocation.Location.Id;
                dbAssignedLocation.Location = assignedLocation.Location;
            }

            if (dbAssignedLocation.ProjectResourceId != dbResource.Id)
            {
                dbAssignedLocation.ProjectResourceId = dbResource.Id;
                dbAssignedLocation.ProjectResource = dbResource;
            }

            if (dbAssignedLocation.AllocationUnitTypeId != dbAllocUnitType.Id)
            {
                dbAssignedLocation.AllocationUnitTypeId = dbAllocUnitType.Id;
                dbAssignedLocation.AllocationUnitType = dbAllocUnitType;
            }

            context.AddRange(newAssignedLocationValues);
            context.RemoveRange(removedAssignedLocationValues);


            return dbAssignedLocation;
        }
        internal async Task EnsureProjectResourceAssignmentAsync(
            Project dbProject,
            ProjectResource dbResource,
            CancellationToken ct)
        {
            var assignmentCollection = context.Entry(dbProject)
                .Collection(project => project.AssignedProjectResources);

            if (!assignmentCollection.IsLoaded)
            {
                await assignmentCollection.LoadAsync(ct);
            }

            var existingAssignment = dbProject.AssignedProjectResources
                .FirstOrDefault(a => a.ProjectResourceId == dbResource.Id);

            if (existingAssignment is null)
            {
                context.Add(new AssignedProjectResourceToProject
                {
                    Project = dbProject,
                    ProjectId = dbProject.Id,
                    ProjectResource = dbResource,
                    ProjectResourceId = dbResource.Id,
                    CreatedAt = DateTime.UtcNow
                });
                return;
            }

            if (existingAssignment.IsDeleted)
            {
                existingAssignment.IsDeleted = false;
                existingAssignment.ModifiedAt = DateTime.UtcNow;
            }
        }
    }

    private static Project CreateProjectForInsert(Project source, ProjectDomainType dbDomainType, User createdByUser) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            ShortName = source.ShortName,
            Description = source.Description,
            Email = source.Email,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            ProjectDomainType = dbDomainType,
            CreatedByUser = createdByUser,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            Mode = source.Mode,
            ServiceName = source.ServiceName,
            ExternalId = source.ExternalId
        };

    private static AssignedLocation CreateAssignedLocationForInsert(
        AssignedLocation source,
        ProjectResource dbResource,
        AllocationUnitType dbAllocUnitType)
    {
        var assignedLocation = new AssignedLocation
        {
            // ID will differ after persistence (source.Id is Guid.Empty); callers re-point to the returned instance.
            Id = source.Id,
            ProjectResourceId = dbResource.Id,
            ProjectResource = dbResource,
            LocationId = source.Location?.Id ?? source.LocationId,
            Location = source.Location,
            AllocationUnitTypeId = dbAllocUnitType.Id,
            AllocationUnitType = dbAllocUnitType,
            AllocationUnitCount = source.AllocationUnitCount,
            Description = source.Description,
            AggregationName = source.AggregationName,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsDeleted = source.IsDeleted,
            Mode = source.Mode,
            ServiceName = source.ServiceName,
            ExternalId = source.ExternalId
        };

        foreach (var sourceValue in source.AssignedLocationsValues)
        {
            assignedLocation.AssignedLocationsValues.Add(new AssignedLocationValue
            {
                // ID will differ after persistence (source.Id is Guid.Empty); callers re-point to the returned instance.
                Id = sourceValue.Id,
                Name = sourceValue.Name,
                Value = sourceValue.Value,
                AssignedLocation = assignedLocation
            });
        }

        return assignedLocation;
    }
}