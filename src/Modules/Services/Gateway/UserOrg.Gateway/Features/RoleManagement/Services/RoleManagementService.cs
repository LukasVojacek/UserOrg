using System.Text.Json;

using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Gateway.Features.RoleManagement.Mappers;

namespace UserOrg.Gateway.Features.RoleManagement.Services;

/// <summary>
/// In-process service implementation coordinating role management operations.
/// Delegates to Forge.Core services and performs DTO mappings for Gateway UI.
/// </summary>
/// <param name="roleService">Service for role operations.</param>
/// <param name="permissionService">Service for permission operations.</param>
/// <param name="endpointPermissionService">Service for endpoint permission mappings.</param>
/// <param name="forgeImportExportFeatures">Collection of import/export feature implementations.</param>
internal sealed class RoleManagementService(
    IRoleService roleService,
    IPermissionService permissionService,
    IEndpointPermissionService endpointPermissionService,
    IEnumerable<IForgeImportExport> forgeImportExportFeatures) : IRoleManagementService
{
    private const string RolesSection = "roles";
    private const string PermissionsSection = "permissions";
    private const string EndpointMappingsSection = "endpointMappings";

    /// <summary>The import features that must be registered, in the order their absence is reported.</summary>
    private static readonly ImportSection[] RequiredImportSections =
    [
        new(RolesSection, "Roles"),
        new(PermissionsSection, "Permissions"),
        new(EndpointMappingsSection, "EndpointMappings"),
    ];

    /// <summary>Permissions first: endpoint mappings and roles reference them.</summary>
    private static readonly string[] ImportOrder = [PermissionsSection, EndpointMappingsSection, RolesSection];

    /// <summary>Nesting limit of an import document; the value the import has always parsed with.</summary>
    private const int ImportMaxDepth = 64;

    private static readonly JsonDocumentOptions ImportDocumentOptions = new()
    {
        MaxDepth = ImportMaxDepth,
        AllowTrailingCommas = true
    };

    public async Task<Result<IReadOnlyCollection<RoleSummary>, Error>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var result = await roleService.ListRoles(cancellationToken);

        if (result.IsSuccess)
        {
            return Result<IReadOnlyCollection<RoleSummary>, Error>.Success(
                [.. result.Value.Select(r => r.ToRoleSummary())]);
        }

        return Result<IReadOnlyCollection<RoleSummary>, Error>.Failure(result.Error);
    }

    public async Task<Result<RoleDetail, Error>> GetRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var result = await roleService.GetRole(roleName, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<RoleDetail, Error>.Success(result.Value.ToRoleDetail());
        }

        return Result<RoleDetail, Error>.Failure(result.Error);
    }

    public async Task<Result<RoleDetail, Error>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var permissionKeys = request.Permissions.ToPermissionKeys();

        var result = await roleService.CreateRole(
            request.Name,
            request.Description ?? string.Empty,
            request.Category,
            request.Level,
            permissionKeys,
            cancellationToken);

        if (result.IsSuccess)
        {
            return Result<RoleDetail, Error>.Success(result.Value.ToRoleDetail());
        }

        return Result<RoleDetail, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> UpdateRoleStatusAsync(string roleName, bool isActive, CancellationToken cancellationToken)
    {
        var result = await roleService.UpdateRoleState(roleName, isActive, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(result.Value);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> UpdateRolePermissionsAsync(string roleName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var permissionKeys = permissions.ToPermissionKeys();

        var result = await roleService.UpdateRolePermissions(roleName, permissionKeys, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> UpdateRoleDescriptionAsync(string roleName, string? description, CancellationToken cancellationToken)
    {
        var result = await roleService.UpdateRoleDescription(roleName, description, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> UpdateRoleLevelAsync(string roleName, int level, CancellationToken cancellationToken)
    {
        var result = await roleService.UpdateRoleLevel(roleName, level, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> UpdateRoleCategoryAsync(string roleName, string category, CancellationToken cancellationToken)
    {
        var result = await roleService.UpdateRoleCategory(roleName, category, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<IReadOnlyCollection<PermissionInfo>, Error>> GetPermissionsAsync(CancellationToken cancellationToken)
    {
        var result = await permissionService.ListPermissions(cancellationToken);

        if (result.IsSuccess)
        {
            return Result<IReadOnlyCollection<PermissionInfo>, Error>.Success(
                [.. result.Value.Select(p => p.ToDto())]);
        }

        return Result<IReadOnlyCollection<PermissionInfo>, Error>.Failure(result.Error);
    }

    public async Task<Result<PermissionInfo, Error>> GetPermissionAsync(string permissionKey, CancellationToken cancellationToken)
    {
        var key = permissionKey.ToPermissionKey();
        if (key is null)
        {
            return Result<PermissionInfo, Error>.Failure(PermissionErrors.InvalidPermissionKey);
        }

        var result = await permissionService.GetPermission(key.Value, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<PermissionInfo, Error>.Success(result.Value.ToDto());
        }

        return Result<PermissionInfo, Error>.Failure(result.Error);
    }

    public async Task<Result<PermissionInfo, Error>> CreatePermissionAsync(string permissionKey, string description, CancellationToken cancellationToken)
    {
        var key = permissionKey.ToPermissionKey();
        if (key is null)
        {
            return Result<PermissionInfo, Error>.Failure(PermissionErrors.InvalidPermissionKey);
        }

        var result = await permissionService.CreatePermission(key.Value, description, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<PermissionInfo, Error>.Success(result.Value.ToDto());
        }

        return Result<PermissionInfo, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> DeletePermissionAsync(string permissionKey, CancellationToken cancellationToken)
    {
        var key = permissionKey.ToPermissionKey();
        if (key is null)
        {
            return Result<bool, Error>.Failure(PermissionErrors.InvalidPermissionKey);
        }

        var result = await permissionService.DeletePermission(key.Value, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> IsPermissionAssignedAsync(string permissionKey, CancellationToken cancellationToken)
    {
        var key = permissionKey.ToPermissionKey();
        if (key is null)
        {
            return Result<bool, Error>.Failure(PermissionErrors.InvalidPermissionKey);
        }

        var result = await permissionService.IsPermissionAssignedToRoles(key.Value, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(result.Value);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<IReadOnlyCollection<EndpointMappingInfo>, Error>> GetEndpointMappingsAsync(CancellationToken cancellationToken)
    {
        // GetEndpointMappingsAsync expects endpointName parameter; empty string retrieves all mappings
        var mappings = await endpointPermissionService.GetEndpointMappingsAsync(string.Empty, cancellationToken);

        return Result<IReadOnlyCollection<EndpointMappingInfo>, Error>.Success(
            [.. mappings.Select(m => m.ToEndpointMappingInfo())]);
    }

    public async Task<Result<bool, Error>> SetEndpointPermissionsAsync(string endpointName, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var endpointMap = EndpointMappers.ToEndpointPermissionMap(endpointName, permissions);

        var result = await endpointPermissionService.SetEndpointPermissionAsync(endpointMap, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public async Task<Result<EndpointMappingInfo, Error>> CreateEndpointMappingAsync(
        string endpointName,
        string pathTemplate,
        string httpMethod,
        string? description,
        CancellationToken cancellationToken)
    {
        var result = await endpointPermissionService.CreateEndpointMappingAsync(
            endpointName, pathTemplate, httpMethod, description, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<EndpointMappingInfo, Error>.Success(result.Value.ToEndpointMappingInfo());
        }

        return Result<EndpointMappingInfo, Error>.Failure(result.Error);
    }

    public async Task<Result<bool, Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken)
    {
        var result = await endpointPermissionService.DeleteEndpointMappingAsync(endpointName, cancellationToken);

        if (result.IsSuccess)
        {
            return Result<bool, Error>.Success(true);
        }

        return Result<bool, Error>.Failure(result.Error);
    }

    public Task InvalidateEndpointPermissionCacheAsync(CancellationToken cancellationToken) =>
        endpointPermissionService.InvalidateSnapshotAsync(cancellationToken);

    public async Task<Result<bool, Error>> ExportRolesAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        try
        {
            var rolesFeature = forgeImportExportFeatures.FirstOrDefault(f =>
                f.FeatureName.Equals("roles", StringComparison.OrdinalIgnoreCase));
            var permissionsFeature = forgeImportExportFeatures.FirstOrDefault(f =>
                f.FeatureName.Equals("permissions", StringComparison.OrdinalIgnoreCase));
            var endpointMappingsFeature = forgeImportExportFeatures.FirstOrDefault(f =>
                f.FeatureName.Equals("endpointMappings", StringComparison.OrdinalIgnoreCase));

            if (rolesFeature is null)
            {
                return Result<bool, Error>.Failure(new Error(
                    "RolesFeatureNotFound",
                    ErrorSeverity.Error,
                    "Roles import/export feature is not registered"));
            }

            if (permissionsFeature is null)
            {
                return Result<bool, Error>.Failure(new Error(
                    "PermissionsFeatureNotFound",
                    ErrorSeverity.Error,
                    "Permissions import/export feature is not registered"));
            }

            if (endpointMappingsFeature is null)
            {
                return Result<bool, Error>.Failure(new Error(
                    "EndpointMappingsFeatureNotFound",
                    ErrorSeverity.Error,
                    "EndpointMappings import/export feature is not registered"));
            }

            var writerOptions = new System.Text.Json.JsonWriterOptions { Indented = true };
            using var writer = new System.Text.Json.Utf8JsonWriter(outputStream, writerOptions);

            writer.WriteStartObject();
            writer.WriteString("formatVersion", "1.0");
            writer.WriteString("exportedAt", DateTimeOffset.UtcNow);

            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("app", "UserOrg.Gateway");
            writer.WriteString("instanceId", Environment.MachineName);
            writer.WriteEndObject();

            writer.WritePropertyName("features");
            writer.WriteStartObject();

            // Export permissions first (roles and endpoint mappings depend on permissions)
            writer.WritePropertyName("permissions");
            var options = new ExportOptions();
            await permissionsFeature.ExportAsync(writer, options, cancellationToken);
            await writer.FlushAsync(cancellationToken);

            // Export endpoint mappings (depends on permissions)
            writer.WritePropertyName("endpointMappings");
            await endpointMappingsFeature.ExportAsync(writer, options, cancellationToken);
            await writer.FlushAsync(cancellationToken);

            // Export roles (depends on permissions)
            writer.WritePropertyName("roles");
            await rolesFeature.ExportAsync(writer, options, cancellationToken);
            await writer.FlushAsync(cancellationToken);

            writer.WriteEndObject(); // end features

            writer.WritePropertyName("metadata");
            writer.WriteStartObject();
            writer.WriteEndObject();

            writer.WriteEndObject(); // end envelope
            await writer.FlushAsync(cancellationToken);

            return Result<bool, Error>.Success(true);
        }
        catch (Exception ex)
        {
            return Result<bool, Error>.Failure(new Error(
                "ExportFailed",
                ErrorSeverity.Error,
                $"Failed to export configuration: {ex.Message}"));
        }
    }

    public async Task<Result<ImportExportSummary, Error>> ImportRolesAsync(Stream inputStream, bool overwriteExisting, CancellationToken cancellationToken)
    {
        try
        {
            var features = ResolveImportFeatures();
            if (!features.IsSuccess)
            {
                return Result<ImportExportSummary, Error>.Failure(features.Error);
            }

            using var document = await JsonDocument.ParseAsync(inputStream, ImportDocumentOptions, cancellationToken);
            var featuresElement = ReadFeaturesElement(document.RootElement);
            if (!featuresElement.IsSuccess)
            {
                return Result<ImportExportSummary, Error>.Failure(featuresElement.Error);
            }

            var importOptions = new ImportOptions(
                FailFast: false,
                OverwriteExisting: overwriteExisting);
            var summary = await ImportSectionsAsync(features.Value, featuresElement.Value, importOptions, cancellationToken);
            return Result<ImportExportSummary, Error>.Success(summary);
        }
        catch (JsonException ex)
        {
            return Result<ImportExportSummary, Error>.Failure(new Error(
                "InvalidJson",
                ErrorSeverity.Error,
                $"Invalid JSON: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result<ImportExportSummary, Error>.Failure(new Error(
                "ImportFailed",
                ErrorSeverity.Error,
                $"Failed to import configuration: {ex.Message}"));
        }
    }

    /// <summary>
    /// Finds the registered feature for every import section (feature names match ignoring case); fails
    /// with <c>{Label}FeatureNotFound</c> for the first section, in <see cref="RequiredImportSections"/>
    /// order, that has none.
    /// </summary>
    private Result<Dictionary<string, IForgeImportExport>, Error> ResolveImportFeatures()
    {
        var resolved = new Dictionary<string, IForgeImportExport>(StringComparer.Ordinal);
        foreach (var section in RequiredImportSections)
        {
            var feature = forgeImportExportFeatures.FirstOrDefault(f =>
                f.FeatureName.Equals(section.Name, StringComparison.OrdinalIgnoreCase));
            if (feature is null)
            {
                return Result<Dictionary<string, IForgeImportExport>, Error>.Failure(new Error(
                    $"{section.Label}FeatureNotFound",
                    ErrorSeverity.Error,
                    $"{section.Label} import/export feature is not registered"));
            }

            resolved[section.Name] = feature;
        }

        return Result<Dictionary<string, IForgeImportExport>, Error>.Success(resolved);
    }

    /// <summary>
    /// Validates the export envelope (a string <c>formatVersion</c> and a <c>features</c> object) and
    /// returns the <c>features</c> object.
    /// </summary>
    private static Result<JsonElement, Error> ReadFeaturesElement(JsonElement root)
    {
        if (!root.TryGetProperty("formatVersion", out var formatVersion) || formatVersion.ValueKind != JsonValueKind.String)
        {
            return InvalidFormat("Missing or invalid 'formatVersion' property");
        }

        if (!root.TryGetProperty("features", out var featuresElement) || featuresElement.ValueKind != JsonValueKind.Object)
        {
            return InvalidFormat("Missing or invalid 'features' object");
        }

        return Result<JsonElement, Error>.Success(featuresElement);
    }

    private static Result<JsonElement, Error> InvalidFormat(string description)
        => Result<JsonElement, Error>.Failure(new Error("InvalidFormat", ErrorSeverity.Error, description));

    /// <summary>
    /// Imports the sections present in <paramref name="featuresElement"/> in <see cref="ImportOrder"/> and
    /// sums their counts and errors into one summary.
    /// </summary>
    private static async Task<ImportExportSummary> ImportSectionsAsync(
        Dictionary<string, IForgeImportExport> features,
        JsonElement featuresElement,
        ImportOptions importOptions,
        CancellationToken cancellationToken)
    {
        var totalCreated = 0;
        var totalUpdated = 0;
        var allErrors = new List<string>();

        foreach (var sectionName in ImportOrder)
        {
            if (!featuresElement.TryGetProperty(sectionName, out var sectionData))
            {
                continue;
            }

            var sectionResult = await features[sectionName].ImportAsync(sectionData, importOptions, cancellationToken);
            totalCreated += sectionResult.CreatedCount;
            totalUpdated += sectionResult.UpdatedCount;
            allErrors.AddRange(sectionResult.Errors ?? []);
        }

        return new ImportExportSummary(
            Success: allErrors.Count == 0,
            CreatedCount: totalCreated,
            UpdatedCount: totalUpdated,
            ErrorCount: allErrors.Count,
            Errors: allErrors.Count > 0 ? allErrors : null);
    }

    private sealed record ImportSection(string Name, string Label);
}