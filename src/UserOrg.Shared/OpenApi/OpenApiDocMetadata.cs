using Microsoft.OpenApi;

namespace UserOrg.Shared.OpenApi;

/// <summary>
/// Endpoint metadata carrying a delegate that customises the generated <see cref="OpenApiOperation"/>.
/// </summary>
/// <remarks>
/// This is the replacement for ASP.NET Core's <c>WithOpenApi(...)</c> convention, which writes its
/// delegate into a metadata slot that the .NET 10 <c>AddOpenApi</c> document pipeline never reads —
/// so every summary, description and parameter description written through it was silently dropped.
/// The metadata is declarative (the same pattern as
/// <see cref="UserOrg.Shared.FeatureManagement.DisabledByFeatureFlagMetadata"/>) and is consumed by
/// an <c>IOpenApiOperationTransformer</c> registered in the API host.
/// </remarks>
public sealed class OpenApiDocMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenApiDocMetadata"/> class.
    /// </summary>
    /// <param name="configure">
    /// Delegate invoked with the generated operation. It is expected to mutate and return that same
    /// instance; a different instance cannot be substituted because the transformer pipeline hands out
    /// the live operation rather than a replaceable reference.
    /// </param>
    public OpenApiDocMetadata(Func<OpenApiOperation, OpenApiOperation> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Configure = configure;
    }

    /// <summary>
    /// Gets the delegate that customises the generated operation.
    /// </summary>
    public Func<OpenApiOperation, OpenApiOperation> Configure { get; }
}