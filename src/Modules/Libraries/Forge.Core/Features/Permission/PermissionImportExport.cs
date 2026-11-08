using System.Text.Json;

using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Role.Services;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Features.Permission;

/// <summary>
/// Import/Export implementation for the Permission feature.
/// Demonstrates best practices for streaming export and robust import with validation.
/// </summary>
internal sealed class PermissionImportExport : TypedForgeImportExport<PermissionImportDto>
{
    private readonly IDbContextFactory<ForgeDbContext> _dbContextFactory;
    private readonly IRoleService _roleService;
    private readonly ILogger<PermissionImportExport> _logger;

    private static readonly string[] MissingItemsError = new[] { "Missing or invalid 'items' array in permissions payload" };

    public PermissionImportExport(
        IDbContextFactory<ForgeDbContext> dbContextFactory,
        IRoleService roleService,
        ILogger<PermissionImportExport> logger)
        : base(null)
    {
        _dbContextFactory = dbContextFactory;
        _roleService = roleService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task InvalidateAffectedCachesAsync(CancellationToken cancellationToken)
        // Role lists embed permission entities (Role.RolePermissions -> ToPermissionInfo), so
        // overwriting a permission's description makes RoleService's cached role lists stale even
        // though PermissionService itself caches nothing (ADR-0084 §1).
        => _roleService.InvalidateRoleListsAsync(cancellationToken);

    // JsonElement overload: explicitly materialize PermissionImportDto from JsonElement to avoid
    // constructor-binding surprises and to ensure converters are applied correctly for PermissionKey.
    /// <inheritdoc />
    protected override Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
    {
        // Lightweight structured debug log instead of Debug.WriteLine to avoid
        // writing to debug console during tests and CI.
        _logger.LogDebug("[PermissionImportExport] ImportCoreAsync(JsonElement) payloadKind={Kind}", payload.ValueKind);
        List<PermissionItemDto>? items = null;
        if (payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            items = new List<PermissionItemDto>();
            foreach (var el in itemsEl.EnumerateArray())
            {
                try
                {
                    string? keyString = null;
                    if (el.TryGetProperty("key", out var keyEl) && keyEl.ValueKind == JsonValueKind.String)
                        keyString = keyEl.GetString();

                    PermissionKey parsedKey = default;
                    if (!PermissionKey.TryParse(keyString, out parsedKey))
                    {
                        // leave parsedKey as default (invalid) so importer records an error
                        parsedKey = default;
                    }

                    string? description = null;
                    if (el.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                        description = descEl.GetString();

                    DateTimeOffset? createdAt = null;
                    if (el.TryGetProperty("createdAt", out var caEl))
                    {
                        if (caEl.ValueKind == JsonValueKind.String && caEl.TryGetDateTimeOffset(out var dtOffset))
                            createdAt = dtOffset;
                        else if (caEl.ValueKind == JsonValueKind.Number && caEl.TryGetInt64(out var unix))
                            createdAt = DateTimeOffset.FromUnixTimeSeconds(unix);
                    }

                    items.Add(new PermissionItemDto(parsedKey, description, createdAt));
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

        var dto = new PermissionImportDto(count, items);
        return ImportAsync(dto, options, cancellationToken);
    }

    public override string FeatureName => "permissions";

    public override async Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Permissions.AsNoTracking();

        if (options.Since.HasValue)
        {
            // Note: CreatedAt is a shadow property, so we need to use EF.Property
            query = query.Where(p => EF.Property<DateTimeOffset>(p, "CreatedAt") >= options.Since.Value);
        }

        var permissions = await query.ToListAsync(cancellationToken);

        writer.WriteStartObject();
        writer.WriteNumber("count", permissions.Count);

        writer.WritePropertyName("items");
        writer.WriteStartArray();

        foreach (var permission in permissions)
        {
            writer.WriteStartObject();
            writer.WriteString("key", permission.Key.Value);
            writer.WriteString("description", permission.Description);

            var createdAt = context.Entry(permission).Property<DateTimeOffset>("CreatedAt").CurrentValue;
            writer.WriteString("createdAt", createdAt);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        _logger.ExportedPermissions(permissions.Count);
    }

    /// <inheritdoc />
    protected override async Task<ImportResult> ImportAsync(PermissionImportDto payload, ImportOptions options, CancellationToken cancellationToken)
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

            var itemsCount = items is ICollection<PermissionItemDto> coll ? coll.Count : items.Count();
            _logger.LogDebug("[PermissionImportExport] Parsed items={Count}; immediateErrors={ErrorsCount}", itemsCount, errors.Count);

            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // item.Key is deserialized to PermissionKey via the converter; validate defensively
                    var key = item.Key;
                    if (string.IsNullOrEmpty(key.Value))
                    {
                        errors.Add($"Invalid permission key");
                        continue;
                    }

                    var description = item.Description;

                    var existing = await context.Permissions
                        .FirstOrDefaultAsync(p => p.Key == key, cancellationToken);

                    if (existing is not null)
                    {
                        if (options.OverwriteExisting)
                        {
                            existing.UpdateDescription(description);
                            updatedCount++;
                            _logger.UpdatedPermission(key.Value);
                        }
                        else
                        {
                            _logger.SkippingExistingPermission(key.Value);
                        }
                    }
                    else
                    {
                        var permission = new Persistence.Permission(key, description);
                        context.Permissions.Add(permission);
                        createdCount++;
                        _logger.CreatedPermission(key.Value);
                    }
                }
                catch (Exception ex)
                {
                    _logger.FailedImportPermissionItem(ex);
                    errors.Add($"Failed to import permission: {ex.Message}");

                    if (options.FailFast)
                        break;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.ImportedPermissions(createdCount, updatedCount, errors.Count);
            _logger.LogDebug("[PermissionImportExport] After save: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            _logger.LogInformation("Permission import result: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            return new ImportResult(
                Success: errors.Count == 0,
                CreatedCount: createdCount,
                UpdatedCount: updatedCount,
                Errors: errors.Count > 0 ? errors : null);
        }
        catch (Exception ex)
        {
            _logger.FailedImportPermissions(ex);
            return new ImportResult(false, Errors: new[] { ex.Message });
        }
    }

    // Rely on the generic TypedForgeImportExport<JsonElement> implementation for JsonElement->TPayload
    // deserialization. Keeping the typed ImportAsync(PermissionImportDto, ...) above for feature logic.
}