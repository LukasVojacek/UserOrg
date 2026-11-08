using Microsoft.AspNetCore.OpenApi;
using Microsoft.FeatureManagement;
using Microsoft.OpenApi;

using UserOrg.Shared.FeatureManagement;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// OpenAPI operation transformer that marks operations disabled by feature flags.
/// </summary>
/// <remarks>
/// This transformer reads endpoint metadata to identify endpoints marked with
/// <see cref="DisabledByFeatureFlagMetadata"/>. When the associated feature flag
/// is enabled, it adds a marker extension to the operation. The document-level
/// transformer then uses this marker to remove the operation from the spec.
/// 
/// This approach is metadata-driven: each endpoint declares its own protection
/// via .DisabledWhen() extension, making the system maintainable and DRY.
/// </remarks>
internal sealed class FeatureFlagOperationTransformer(IFeatureManager featureManager) : IOpenApiOperationTransformer
{

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (metadata is null)
        {
            return;
        }

        var disabledMetadata = metadata.OfType<DisabledByFeatureFlagMetadata>().FirstOrDefault();
        if (disabledMetadata is null)
        {
            return;
        }

        var isFlagEnabled = await featureManager.IsEnabledAsync(disabledMetadata.FeatureFlagName);
        if (isFlagEnabled)
        {
            operation.Deprecated = true;
            if (operation.Description is not null)
            {
                operation.Description = $"[DISABLED] {operation.Description}";
            }
            else
            {
                operation.Description = "[DISABLED] This endpoint is disabled by a feature flag.";
            }
        }
    }
}