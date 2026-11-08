using Microsoft.OpenApi;

using UserOrg.Shared.OpenApi;

// Deliberately placed in the framework's own convention-builder namespace, exactly like ASP.NET Core's
// OpenApiRouteHandlerBuilderExtensions. Every existing `.WithOpenApi(...)` call site already resolves
// that namespace (implicit usings in the Web SDK, an explicit `using Microsoft.AspNetCore.Builder;` in
// the Forge.Core module), so renaming the call sites needs no using-directive edits anywhere.
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods that attach OpenAPI documentation delegates to an endpoint.
/// </summary>
public static class OpenApiDocEndpointConventionBuilderExtensions
{
    /// <summary>
    /// Registers a delegate that customises the OpenAPI operation generated for this endpoint.
    /// </summary>
    /// <remarks>
    /// Drop-in replacement for <c>WithOpenApi(Func&lt;OpenApiOperation, OpenApiOperation&gt;)</c>, which is
    /// inert under the .NET 10 <c>AddOpenApi</c> document pipeline. The delegate must mutate and return the
    /// operation it is given; returning a different instance has no effect.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <param name="configureOperation">The delegate applied to the generated operation.</param>
    /// <returns>The endpoint convention builder, for chaining.</returns>
    public static TBuilder WithOpenApiDoc<TBuilder>(
        this TBuilder builder,
        Func<OpenApiOperation, OpenApiOperation> configureOperation)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configureOperation);

        return builder.WithMetadata(new OpenApiDocMetadata(configureOperation));
    }

    /// <summary>
    /// No-op overload kept so that bare <c>WithOpenApi()</c> call sites can be renamed uniformly.
    /// </summary>
    /// <remarks>
    /// The parameterless form never carried documentation of its own — it only asked the old pipeline to
    /// emit an operation, which <c>AddOpenApi</c> does unconditionally. It is retained purely so the rename
    /// is mechanical; it adds no metadata.
    /// </remarks>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint convention builder.</param>
    /// <returns>The endpoint convention builder, for chaining.</returns>
    public static TBuilder WithOpenApiDoc<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder;
    }
}