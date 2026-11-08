using System.Text.Json;

using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;
using Forge.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forge.Core.Features.Role;

/// <summary>
/// Import/Export implementation for the Role feature.
/// Exports roles with their associated permissions and supports importing roles with validation.
/// </summary>
internal sealed class RoleImportExport : TypedForgeImportExport<RoleImportDto>
{
    private readonly IDbContextFactory<ForgeDbContext> _dbContextFactory;
    private readonly IRoleService _roleService;
    private readonly ILogger<RoleImportExport> _logger;

    private static readonly string[] MissingItemsError = new[] { "Missing or invalid 'items' array in roles payload" };

    public RoleImportExport(
        IDbContextFactory<ForgeDbContext> dbContextFactory,
        IRoleService roleService,
        ILogger<RoleImportExport> logger)
        : base(null)
    {
        _dbContextFactory = dbContextFactory;
        _roleService = roleService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task InvalidateAffectedCachesAsync(CancellationToken cancellationToken)
        => _roleService.InvalidateRoleListsAsync(cancellationToken);

    /// <inheritdoc />
    protected override Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[RoleImportExport] ImportCoreAsync(JsonElement) payloadKind={Kind}", payload.ValueKind);
        List<RoleItemDto>? items = null;
        if (payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            items = new List<RoleItemDto>();
            foreach (var el in itemsEl.EnumerateArray())
            {
                try
                {
                    string? name = null;
                    if (el.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        name = nameEl.GetString();

                    string? description = null;
                    if (el.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String)
                        description = descEl.GetString();

                    string? category = null;
                    if (el.TryGetProperty("category", out var catEl) && catEl.ValueKind == JsonValueKind.String)
                        category = catEl.GetString();

                    bool isActive = true;
                    if (el.TryGetProperty("isActive", out var activeEl) && (activeEl.ValueKind == JsonValueKind.True || activeEl.ValueKind == JsonValueKind.False))
                        isActive = activeEl.GetBoolean();

                    int level = 1;
                    if (el.TryGetProperty("level", out var levelEl) && levelEl.ValueKind == JsonValueKind.Number)
                        level = levelEl.GetInt32();

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

                    if (!string.IsNullOrEmpty(name))
                    {
                        items.Add(new RoleItemDto(name, description, category, isActive, level, permissionKeys, createdAt));
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

        var dto = new RoleImportDto(count, items);
        return ImportAsync(dto, options, cancellationToken);
    }

    public override string FeatureName => "roles";

    public override async Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken)
    {
        using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = context.Roles
            .Include(r => r.RolePermissions)
            .AsNoTracking();

        if (options.Since.HasValue)
        {
            query = query.Where(r => EF.Property<DateTimeOffset>(r, "CreatedAt") >= options.Since.Value);
        }

        var roles = await query.ToListAsync(cancellationToken);

        writer.WriteStartObject();
        writer.WriteNumber("count", roles.Count);

        writer.WritePropertyName("items");
        writer.WriteStartArray();

        foreach (var role in roles)
        {
            writer.WriteStartObject();
            writer.WriteString("name", role.Name);
            writer.WriteString("description", role.Description);
            writer.WriteString("category", role.Category);
            writer.WriteBoolean("isActive", role.IsActive);
            writer.WriteNumber("level", role.Level);

            writer.WritePropertyName("permissionKeys");
            writer.WriteStartArray();
            foreach (var permission in role.RolePermissions)
            {
                writer.WriteStringValue(permission.Key.Value);
            }
            writer.WriteEndArray();

            var createdAt = context.Entry(role).Property<DateTimeOffset>("CreatedAt").CurrentValue;
            writer.WriteString("createdAt", createdAt);

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();

        _logger.ExportedRoles(roles.Count);
    }

    /// <inheritdoc />
    protected override async Task<ImportResult> ImportAsync(RoleImportDto payload, ImportOptions options, CancellationToken cancellationToken)
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

            var itemsCount = items is ICollection<RoleItemDto> coll ? coll.Count : items.Count();
            _logger.LogDebug("[RoleImportExport] Parsed items={Count}; immediateErrors={ErrorsCount}", itemsCount, errors.Count);

            using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            // Load all permissions once to avoid N+1 queries
            var allPermissions = await context.Permissions.ToDictionaryAsync(p => p.Key.Value, p => p, cancellationToken);

            foreach (var item in items)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    if (string.IsNullOrEmpty(item.Name))
                    {
                        errors.Add("Invalid role name");
                        continue;
                    }

                    var existing = await context.Roles
                        .Include(r => r.RolePermissions)
                        .FirstOrDefaultAsync(r => r.Name == item.Name, cancellationToken);

                    if (existing is not null)
                    {
                        if (options.OverwriteExisting)
                        {
                            existing.Description = item.Description;
                            existing.Category = item.Category ?? string.Empty;
                            existing.IsActive = item.IsActive;
                            existing.Level = item.Level;

                            existing.RolePermissions.Clear();
                            if (item.PermissionKeys is not null)
                            {
                                foreach (var permKey in item.PermissionKeys)
                                {
                                    if (allPermissions.TryGetValue(permKey, out var permission))
                                    {
                                        existing.RolePermissions.Add(permission);
                                    }
                                    else
                                    {
                                        _logger.LogWarning("Permission {PermissionKey} not found for role {RoleName}", permKey, item.Name);
                                        errors.Add($"Permission '{permKey}' not found during import of role '{item.Name}'");
                                    }
                                }
                            }

                            updatedCount++;
                            _logger.UpdatedRole(item.Name);
                        }
                        else
                        {
                            _logger.SkippingExistingRole(item.Name);
                        }
                    }
                    else
                    {
                        var newRole = new Persistence.Role
                        {
                            Name = item.Name,
                            Description = item.Description,
                            Category = item.Category ?? string.Empty,
                            IsActive = item.IsActive,
                            Level = item.Level,
                            RolePermissions = new List<Permission.Persistence.Permission>()
                        };

                        if (item.PermissionKeys is not null)
                        {
                            foreach (var permKey in item.PermissionKeys)
                            {
                                if (allPermissions.TryGetValue(permKey, out var permission))
                                {
                                    newRole.RolePermissions.Add(permission);
                                }
                                else
                                {
                                    _logger.LogWarning("Permission {PermissionKey} not found for role {RoleName}", permKey, item.Name);
                                    errors.Add($"Permission '{permKey}' not found during import of role '{item.Name}'");
                                }
                            }
                        }

                        context.Roles.Add(newRole);
                        createdCount++;
                        _logger.CreatedRole(item.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.FailedImportRoleItem(ex);
                    errors.Add($"Failed to import role: {ex.Message}");

                    if (options.FailFast)
                        break;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            _logger.ImportedRoles(createdCount, updatedCount, errors.Count);
            _logger.LogDebug("[RoleImportExport] After save: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            _logger.LogInformation("Role import result: created={Created} updated={Updated} errors={Errors}", createdCount, updatedCount, string.Join(';', errors));

            return new ImportResult(
                Success: errors.Count == 0,
                CreatedCount: createdCount,
                UpdatedCount: updatedCount,
                Errors: errors.Count > 0 ? errors : null);
        }
        catch (Exception ex)
        {
            _logger.FailedImportRoles(ex);
            return new ImportResult(false, Errors: new[] { ex.Message });
        }
    }
}