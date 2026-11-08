using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Userorg.Adapter.Waldur.Base;
using Userorg.Adapter.Waldur.Base.Orchestrators.Extensions;
using Userorg.Adapter.Waldur.Base.Sync;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Domain.Entities;

using Waldur.Contracts.Events;
using Waldur.Contracts.Payloads;

namespace Userorg.Adapter.Waldur.Sync.Handlers;

/// <summary>
/// Consumer-side upsert for a Waldur provider (customer) and its derived locations (ADR-0057,
/// plan §4.2). Both <see cref="ProviderCreated"/> and <see cref="ProviderUpdated"/> route to the
/// SAME upsert method — the Created/Updated distinction is not load-bearing (ADR-0057/A1), a wrong
/// guess against a populated DB must not change behaviour.
/// </summary>
/// <remarks>
/// Ports the old <c>WaldurEfpExporter.ExportData(ProviderDto)</c> / <c>EnsureProviderAsync</c> path:
/// provider keyed <c>(ExternalId, ServiceName)</c>, Country + LocationType resolved from the DB
/// (missing → throw), derived Location upsert by endpoint+service with an endpoint-only fallback
/// (ambiguity → throw). All missing/ambiguous cases throw so the event is NOT acked and replays
/// (ADR-0057). One terminal <see cref="IUserOrgDbContext.SaveChangesAsync"/> gives atomicity for
/// free (ADR-0057/A3) — no manual transaction. The service account is LOOKED UP (never ensured —
/// B06/F1) and used as the provider's responsible user.
/// </remarks>
internal sealed class ProviderUpsertHandler(IUserOrgDbContext context, ILogger<ProviderUpsertHandler> logger)
    : IWaldurEventHandler<ProviderCreated>, IWaldurEventHandler<ProviderUpdated>
{
    public Task<Result<Error>> HandleAsync(ProviderCreated evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return UpsertAsync(evt.Payload, evt.Instance, evt.ServiceName, ct);
    }

    public Task<Result<Error>> HandleAsync(ProviderUpdated evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return UpsertAsync(evt.Payload, evt.Instance, evt.ServiceName, ct);
    }

    private async Task<Result<Error>> UpsertAsync(ProviderPayload payload, string integrationName, string serviceName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var accountResult = await context.RequireServiceAccountAsync(integrationName, serviceName, ct);
        if (!accountResult.IsSuccess)
        {
            return Result<Error>.Failure(accountResult.Error);
        }

        var waldurUser = accountResult.Value;

        var provider = MapProvider(payload, serviceName);
        var countryResult = await context.RequireCountryAsync(logger, integrationName, provider.Country!, ct);
        if (!countryResult.IsSuccess)
        {
            return Result<Error>.Failure(countryResult.Error);
        }

        var dbProvider = await context.UpsertProviderAsync(logger, integrationName, provider, countryResult.Value, waldurUser, ct);

        foreach (var locationPayload in payload.Locations)
        {
            var location = MapLocation(locationPayload, serviceName);
            var locationTypeResult = await context.RequireLocationTypeAsync(logger, integrationName, location.LocationType!, ct);
            if (!locationTypeResult.IsSuccess)
            {
                return Result<Error>.Failure(locationTypeResult.Error);
            }

            var upsertResult = await UpsertLocationAsync(location, dbProvider, locationTypeResult.Value, integrationName, ct);
            if (!upsertResult.IsSuccess)
            {
                return upsertResult;
            }
        }

        // Single terminal save = atomicity without a manual transaction (ADR-0057/A3).
        await context.SaveChangesAsync(ct);
        return Result<Error>.Success();
    }

    /// <summary>
    /// Upserts a derived location keyed <c>(Endpoint, ServiceName)</c> with an endpoint-only
    /// fallback; more than one match at either level is ambiguous → <see cref="Result{TError}"/>
    /// failure (plan §identity table, ADR-0065 — an expected failure, not a throw).
    /// </summary>
    private async Task<Result<Error>> UpsertLocationAsync(
        Location location,
        Provider dbProvider,
        LocationType dbLocationType,
        string integrationName,
        CancellationToken ct)
    {
        Location? existing = null;

        // A Location belongs to a provider — scope BOTH the (endpoint, service) match and the
        // endpoint-only fallback to THIS provider (review 2a). Otherwise the fallback could match
        // and mutate a location owned by a DIFFERENT provider/service (SyncFrom leaves
        // ProviderId/ServiceName untouched, so its natural key would never converge). A null/empty
        // endpoint cannot be matched by endpoint — insert as new to avoid a permanently-stuck poison
        // event (review 2b); the mapper is expected to always derive a non-empty endpoint.
        if (!string.IsNullOrEmpty(location.Endpoint))
        {
            var endpointAndServiceMatches = await context.LocationsQuery
                .Where(l => l.ProviderId == dbProvider.Id && l.Endpoint == location.Endpoint && l.ServiceName == location.ServiceName)
                .ToListAsync(ct);
            var endpointAndServiceResult = ResolveSingle(endpointAndServiceMatches, location.Endpoint, integrationName);
            if (!endpointAndServiceResult.IsSuccess)
            {
                return Result<Error>.Failure(endpointAndServiceResult.Error);
            }

            existing = endpointAndServiceResult.Value;

            if (existing is null)
            {
                var endpointMatches = await context.LocationsQuery
                    .Where(l => l.ProviderId == dbProvider.Id && l.Endpoint == location.Endpoint)
                    .ToListAsync(ct);
                var endpointResult = ResolveSingle(endpointMatches, location.Endpoint, integrationName);
                if (!endpointResult.IsSuccess)
                {
                    return Result<Error>.Failure(endpointResult.Error);
                }

                existing = endpointResult.Value;
            }
        }

        if (existing is null)
        {
            context.Add(new Location
            {
                Id = Guid.CreateVersion7(),
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
                ServiceName = location.ServiceName,
            });
            return Result<Error>.Success();
        }

        if (existing.SyncFrom(location, dbLocationType))
        {
            context.MarkAsModified(existing);
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Collapses a scoped location match set to a single-or-null result; more than one match at a
    /// given level is ambiguous → <see cref="Result{TValue,TError}"/> failure (ADR-0065).
    /// </summary>
    private static Result<Location?, Error> ResolveSingle(List<Location> matches, string endpoint, string integrationName)
    {
        if (matches.Count > 1)
        {
            return Result<Location?, Error>.Failure(
                Errors.EnsureProviderFailed(integrationName, $"ambiguous location for endpoint '{endpoint}'."));
        }

        return Result<Location?, Error>.Success(matches.Count == 1 ? matches[0] : null);
    }

    private static Provider MapProvider(ProviderPayload payload, string serviceName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = payload.Name,
            Description = payload.Description,
            Country = new Country { Name = payload.Country },
            IsPrivate = false,
            Mode = EOperationMode.ReadOnly,
            ResponsibleUsers = [],
            ServiceName = serviceName,
            ExternalId = payload.ExternalId,
            CreatedAt = payload.CreatedAt ?? DateTime.UtcNow,
        };

    private static Location MapLocation(ProviderLocationPayload payload, string serviceName) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Name = payload.Name,
            Description = payload.Description,
            Endpoint = payload.Endpoint,
            IsPrivate = false,
            IsShared = payload.IsShared,
            LocationType = new LocationType { Name = payload.LocationType },
            Mode = EOperationMode.ReadOnly,
            ServiceName = serviceName,
            IsDeleted = payload.IsDeleted,
            CreatedAt = DateTime.UtcNow,
        };
}