using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base.Sync;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

namespace Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;

internal static class WaldurProviderContextExtensions
{
    extension(IUserOrgDbContext context)
    {
        internal async Task<Result<Country, Error>> RequireCountryAsync(
            ILogger logger,
            string integrationName,
            Country country,
            CancellationToken ct)
        {
            var dbCountry = await context.CountriesQuery
                .FirstOrDefaultAsync(c => c.Name == country.Name, ct);
            if (dbCountry is not null)
            {
                return Result<Country, Error>.Success(dbCountry);
            }

            logger.CountryNotFoundDebug(integrationName, country.Name);
            logger.CountryNotFound(integrationName, country.Name);
            return Result<Country, Error>.Failure(
                Errors.EnsureProviderFailed(country.Name!, $"Country not found for name: {country.Name}"));
        }

        internal async Task<Result<User, Error>> RequireServiceAccountAsync(
            string integrationName,
            string serviceName,
            CancellationToken ct)
        {
            var serviceAccount = await context.UsersQuery
                .FirstOrDefaultAsync(u => u.IdentityProviderUserId == serviceName, ct);
            if (serviceAccount is not null)
            {
                return Result<User, Error>.Success(serviceAccount);
            }

            return Result<User, Error>.Failure(Errors.ServiceAccountNotBootstrapped(integrationName, serviceName));
        }

        internal async Task<Provider> UpsertProviderAsync(
            ILogger logger,
            string integrationName,
            Provider provider,
            Country dbCountry,
            User dbWaldurUser,
            CancellationToken ct)
        {
            var dbProvider = await context.ProvidersQuery.FirstOrDefaultAsync(p => p.ExternalId == provider.ExternalId && p.ServiceName == provider.ServiceName, ct);
            if (dbProvider is null)
            {
                var providerToInsert = new Provider
                {
                    // ID will differ after persistence (source.Id is Guid.Empty); callers re-point to the returned instance.
                    Id = provider.Id,
                    Name = provider.Name,
                    Description = provider.Description,
                    IsPrivate = provider.IsPrivate,
                    SupportEmail = provider.SupportEmail,
                    Country = dbCountry,
                    ResponsibleUsers = [dbWaldurUser],
                    CreatedAt = provider.CreatedAt,
                    IsDeleted = provider.IsDeleted,
                    Mode = provider.Mode,
                    ServiceName = provider.ServiceName,
                    ExternalId = provider.ExternalId
                };
                context.Add(providerToInsert);
                return providerToInsert;
            }

            if (dbProvider.SyncFrom(provider, dbCountry))
            {
                context.MarkAsModified(dbProvider);

            }

            return dbProvider;
        }

        internal async Task<Result<LocationType, Error>> RequireLocationTypeAsync(
            ILogger logger,
            string integrationName,
            LocationType locationType,
            CancellationToken ct)
        {
            var dbLocationType = await context.LocationTypesQuery
                .FirstOrDefaultAsync(lt => lt.Name == locationType.Name, ct);
            if (dbLocationType is not null)
            {
                return Result<LocationType, Error>.Success(dbLocationType);
            }

            logger.LocationTypeNotFoundDebug(integrationName, locationType.Name);
            logger.LocationTypeNotFound(integrationName, locationType.Name);
            return Result<LocationType, Error>.Failure(
                Errors.EnsureProviderFailed(locationType.Name!, $"LocationType not found for name: {locationType.Name}"));
        }

        internal async Task UpsertLocationAsync(
            Location location,
            Provider dbProvider,
            LocationType dbLocationType,
            CancellationToken ct)
        {
            var dbLocation = await context.LocationsQuery
                .FirstOrDefaultAsync(l => l.Endpoint == location.Endpoint && l.ServiceName == location.ServiceName, ct);
            if (dbLocation is null)
            {
                var locationToInsert = new Location
                {
                    // ID will differ after persistence (source.Id is Guid.Empty); callers re-point to the returned instance.
                    Id = location.Id,
                    Name = location.Name,
                    Description = location.Description,
                    Endpoint = location.Endpoint,
                    IsPrivate = location.IsPrivate,
                    IsShared = location.IsShared,
                    DefaultServiceIntegrationUrl = location.DefaultServiceIntegrationUrl,
                    ProviderId = dbProvider.Id,
                    LocationTypeId = dbLocationType.Id,
                    CreatedAt = location.CreatedAt,
                    IsDeleted = location.IsDeleted,
                    Mode = location.Mode,
                    ServiceName = location.ServiceName
                };
                context.Add(locationToInsert);
                return;
            }

            if (dbLocation.SyncFrom(location, dbLocationType))
            {
                context.MarkAsModified(dbLocation);
            }
        }
    }
}