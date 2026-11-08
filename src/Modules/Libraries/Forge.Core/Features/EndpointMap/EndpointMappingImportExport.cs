using System.Text.Json;

using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Features.EndpointMap;

/// <summary>
/// Import/Export implementation for the EndpointMapping feature.
/// Exports endpoint mappings with their associated permissions and supports importing with validation.
/// </summary>
internal sealed class EndpointMappingImportExport : TypedForgeImportExport<EndpointMappingImportDto>
{
    private readonly IDbContextFactory<ForgeDbContext> _dbContextFactory;
    private readonly IEndpointPermissionService _endpointPermissionService;
    private readonly ILogger<EndpointMappingImportExport> _logger;

    private static readonly string[] MissingItemsError = new[] { "Missing or invalid 'items' array in endpoint mappings payload" };

    public EndpointMappingImportExport(
        IDbContextFactory<ForgeDbContext> dbContextFactory,
        IEndpointPermissionService endpointPermissionService,
        ILogger<EndpointMappingImportExport> logger)
        : base(null)
    {
        _dbContextFactory = dbContextFactory;
        _endpointPermissionService = endpointPermissionService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task InvalidateAffectedCachesAsync(CancellationToken cancellationToken)
        => _endpointPermissionService.InvalidateSnapshotAsync(cancellationToken);

    /// <inheritdoc />
    protected override Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[EndpointMappingImportExport] ImportCoreAsync(JsonElement) payloadKind={Kind}", payload.ValueKind);
        List<EndpointMappingItemDto>? items = null;
        if (payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            items = new List<EndpointMappingItemDto>();
            foreach (var el in itemsEl.EnumerateArray())
            {
                try
                {
                    string? endpointName = null;
                    if (el.TryGetProperty("endpointName", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        endpointName = nameEl.GetString();

                    string? description = null;
                    if (el.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                        description = descEl.GetString();

                    string? pathTemplate = null;
                    if (el.TryGetProperty("pathTemplate", out var pathEl) && pathEl.ValueKind == JsonValueKind.String)
                        pathTemplate = pathEl.GetString();

                    string? httpMethod = null;
                    if (el.TryGetProperty("httpMethod", out var methodEl) && methodEl.ValueKind == JsonValueKind.String)
                        httpMethod = methodEl.GetString();

                    List<string>? permissionKeys = null;
                    if (el.TryGetProperty("permissionKeys", out var permsEl) && permsEl.ValueKind == JsonValueKind.Array)
                    {
                        permissionKeys = new List<string>();
                        foreach (var permEl in permsEl.EnumerateArray())
                        {
                            if (permEl.ValueKind == JsonValueKind.String)
                            {
                                var key = permEl.GetString();
                                if (!string.IsNullOrEmpty(key))
                                    permissionKeys.Add(key);
                            }
                        }
                    }

                    DateTimeOffset? createdAt = null;
                    if (el.TryGetProperty("createdAt", out var caEl))
                    {
                        if (caEl.ValueKind == JsonValueKind.String && caEl.TryGetDateTimeOffset(out var dtOffset))
                            createdAt = dtOffset;
                        else if (caEl.ValueKind == JsonValueKind.Number && caEl.TryGetInt64(out var unix))
                            createdAt = DateTimeOffset.FromUnixTimeSeconds(unix);
                    }

                    if (!string.IsNullOrEmpty(endpointName) && !string.IsNullOrEmpty(pathTemplate) && !string.IsNullOrEmpty(httpMethod))
                    {
                        items.Add(new EndpointMappingItemDto(endpointName, description, pathTemplate, httpMethod, permissionKeys, createdAt));
                    }
                }
                catch
                {
                    // ignore malformed item
                }
            }
        }

        int? count = null;
        if (payload.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number)
            count = countEl.GetInt32();

        var dto = new EndpointMappingImportDto(count, items);
        return ImportAsync(dto, options, cancellationToken);
    }

    public override string FeatureName => "endpointMappings";

    public override async Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.EndpointMappings
            .Include(e => e.Permissions)
            .AsNoTracking();

        if (options.Since.HasValue)
        {
            query = query.Where(e => EF.Property<DateTimeOffset>(e, "CreatedAt") >= options.Since.Value);
        }

        var endpointMappings = await query.ToListAsync(cancellationToken);

        writer.WriteStartObject();
        writer.WriteNumber("count", endpointMappings.Count);

        writer.WritePropertyName("items");
        writer.WriteStartArray();

        foreach (var endpoint in endpointMappings)
        {
            writer.WriteStartObject();
            writer.WriteString("endpointName", endpoint.EndpointName);
            writer.WriteString("description", endpoint.Description);
            writer.WriteString("pathTemplate", endpoint.PathTemplate);
            writer.WriteString("httpMethod", endpoint.HttpMethod);

            writer.WritePropertyName("permissionKeys");
            writer.WriteStartArray();
            foreach (var permission in endpoint.Permissions)
            {
                writer.WriteStringValue(permission.Key.Value);
            }
            writer.WriteEndArray();

            var createdAt = context.Entry(endpoint).Property<DateTimeOffset>("CreatedAt").CurrentValue;
            writer.WriteString("createdAt", createdAt);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        _logger.ExportedEndpointMappings(endpointMappings.Count);
    }

    /// <inheritdoc />
    protected override async Task<ImportResult> ImportAsync(EndpointMappingImportDto payload, ImportOptions options, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var createdCount = 0;
        var updatedCount = 0;

        try
        {
            var items = payload?.Items;

            if (items is null)
            {
                return new ImportResult(false, Errors: MissingItemsError);
            }

            var itemsCount = items is ICollection<EndpointMappingItemDto> coll ? coll.Count : items.Count();
            _logger.LogDebug("[EndpointMappingImportExport] Parsed items={Count}; immediateErrors={ErrorsCount}", itemsCount, errors.Count);

            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    if (string.IsNullOrEmpty(item.EndpointName))
                    {
                        errors.Add("Invalid endpoint name");
                        continue;
                    }

                    if (string.IsNullOrEmpty(item.PathTemplate))
                    {
                        errors.Add($"Invalid path template for endpoint '{item.EndpointName}'");
                        continue;
                    }

                    if (string.IsNullOrEmpty(item.HttpMethod))
                    {
                        errors.Add($"Invalid HTTP method for endpoint '{item.EndpointName}'");
                        continue;
                    }

                    var existing = await context.EndpointMappings
                        .Include(e => e.Permissions)
                        .FirstOrDefaultAsync(e => e.EndpointName == item.EndpointName, cancellationToken);

                    if (existing is not null)
                    {
                        if (options.OverwriteExisting)
                        {
                            existing.Description = item.Description;
                            existing.PathTemplate = item.PathTemplate;
                            existing.HttpMethod = item.HttpMethod.ToUpperInvariant();

                            if (item.PermissionKeys is not null)
                            {
                                var permissionKeys = item.PermissionKeys
                                    .Select(k => PermissionKey.TryParse(k, out var pk) ? pk : default)
                                    .Where(pk => !string.IsNullOrEmpty(pk.Value))
                                    .ToList();

                                var permissions = await context.Permissions
                                    .Where(p => permissionKeys.Contains(p.Key))
                                    .ToListAsync(cancellationToken);

                                existing.Permissions.Clear();
                                foreach (var permission in permissions)
                                {
                                    existing.Permissions.Add(permission);
                                }

                                if (permissions.Count != permissionKeys.Count)
                                {
                                    var missingCount = permissionKeys.Count - permissions.Count;
                                    _logger.LogWarning("Endpoint '{EndpointName}': {MissingCount} permissions not found", item.EndpointName, missingCount);
                                }
                            }

                            updatedCount++;
                            _logger.UpdatedEndpointMapping(item.EndpointName);
                        }
                        else
                        {
                            _logger.SkippingExistingEndpointMapping(item.EndpointName);
                        }
                    }
                    else
                    {
                        var endpointMapping = new Persistence.EndpointMapping
                        {
                            EndpointName = item.EndpointName,
                            Description = item.Description,
                            PathTemplate = item.PathTemplate,
                            HttpMethod = item.HttpMethod.ToUpperInvariant(),
                            Permissions = new List<Permission.Persistence.Permission>()
                        };

                        if (item.PermissionKeys is not null)
                        {
                            var permissionKeys = item.PermissionKeys
                                .Select(k => PermissionKey.TryParse(k, out var pk) ? pk : default)
                                .Where(pk => !string.IsNullOrEmpty(pk.Value))
                                .ToList();

                            var permissions = await context.Permissions
                                .Where(p => permissionKeys.Contains(p.Key))
                                .ToListAsync(cancellationToken);

                            foreach (var permission in permissions)
                            {
                                endpointMapping.Permissions.Add(permission);
                            }

                            if (permissions.Count != permissionKeys.Count)
                            {
                                var missingCount = permissionKeys.Count - permissions.Count;
                                _logger.LogWarning("Endpoint '{EndpointName}': {MissingCount} permissions not found during creation", item.EndpointName, missingCount);
                            }
                        }

                        context.EndpointMappings.Add(endpointMapping);
                        createdCount++;
                        _logger.CreatedEndpointMapping(item.EndpointName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.FailedImportEndpointMappingItem(ex);
                    errors.Add($"Failed to import endpoint mapping '{item.EndpointName}': {ex.Message}");

                    if (options.FailFast)
                        break;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.ImportedEndpointMappings(createdCount, updatedCount, errors.Count);
            _logger.LogDebug("[EndpointMappingImportExport] After save: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            _logger.LogInformation("EndpointMapping import result: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            return new ImportResult(
                Success: errors.Count == 0,
                CreatedCount: createdCount,
                UpdatedCount: updatedCount,
                Errors: errors.Count > 0 ? errors : null);
        }
        catch (Exception ex)
        {
            _logger.FailedImportEndpointMappings(ex);
            return new ImportResult(false, Errors: new[] { ex.Message });
        }
    }
}