using Core.Composition;
using Core.DataAccess;
using Core.DataAccess.Outbox;
using Core.DataAccess.ResourceRequests;

using Forge.Core;

using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;

using UserOrg.Shared.OpenApi;

using UserOrganizationManagement.API.OpenApi;

using WorkflowEngine.Core.DependencyInjection;

namespace UserOrganizationManagement.API.Composition;

internal static class V2CoreExtensions
{
    /// <summary>
    /// Registers modular Core API services and persistence units, conditionally gated by the v2 feature flag.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="isV2ApiEnabled">
    /// Feature flag state read from <c>FeatureManagement:V2Api</c> at startup. When <c>false</c>,
    /// only unconditional registrations run; the v2 endpoints are not mapped, and the v2-only persistence
    /// and background-worker services are not registered. When <c>true</c>, all services and persistence
    /// units are registered.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Unconditional registrations (run regardless of <c>isV2ApiEnabled</c>):</strong>
    /// <list type="bullet">
    ///   <item>
    ///     <c>AddCoreDataAccess</c>: Owns the shared <c>dbo</c> schema migrations (per ADR-0049). The legacy
    ///     v1 API tables live in this schema; the module must initialize the schema even when v2 is off.
    ///   </item>
    ///   <item>
    ///     <c>AddForgeCore</c>: The legacy v1 API injects <c>IRoleListService</c> and <c>IUserOrgRolesService</c>
    ///     from Forge; these services must always be available.
    ///   </item>
    ///   <item>
    ///     The v2 OpenAPI documents (<c>v2-User</c>, <c>v2-ProviderAdmin</c>, <c>v2-Admin</c>):
    ///     Scalar offers every document ApiDocuments.Published lists; they must be resolvable even when v2 is off
    ///     (an empty document is acceptable; an unregistered one crashes the docs UI).
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Conditional registrations (only when <c>isV2ApiEnabled</c> is <c>true</c>):</strong>
    /// <c>AddCore</c>, <c>AddOutboxPersistence</c>, <c>AddResourceRequestsPersistence</c>, and <c>AddWorkflowEngine</c>.
    /// </para>
    /// </remarks>
    internal static WebApplicationBuilder AddV2Core(this WebApplicationBuilder builder, bool isV2ApiEnabled)
    {
        // Sole runtime owner of the dbo migrations since ADR-0049 retired the legacy migrator, and the
        // v1 tables live in that schema.
        builder.Services.AddCoreDataAccess(builder.Configuration);
        // v1 endpoints and services inject Forge's IRoleListService / IUserOrgRolesService directly.
        builder.AddForgeCore();

        // v2 specs grouped by static audience tag; kept apart from the legacy specs by the "api/v2/" route
        // prefix. Scalar offers every document ApiDocuments.Published lists, so the documents must stay resolvable even
        // with v2 off — an empty document is fine, an unregistered one is a 500 in the docs UI.
        builder.Services.AddOpenApi(ApiDocuments.V2User, ConfigureV2OpenApi(ApiAudience.User));
        builder.Services.AddOpenApi(ApiDocuments.V2ProviderAdmin, ConfigureV2OpenApi(ApiAudience.ProviderAdmin));
        builder.Services.AddOpenApi(ApiDocuments.V2Admin, ConfigureV2OpenApi(ApiAudience.Admin));

        if (isV2ApiEnabled)
        {
            builder.Services.AddCore(builder.Configuration);
            builder.AddOutboxPersistence();
            // Resource-request projection is its own schema-isolated persistence unit (ADR-0063 D7, HEAppE pattern).
            builder.AddResourceRequestsPersistence();
            // WorkflowEngine persistence/workers are a host concern (ADR-0056), wired here rather than inside AddCore.
            builder.AddWorkflowEngine();
        }

        return builder;
    }

    internal static WebApplication MapV2CoreEndpoints(this WebApplication app)
    {
        app.MapCoreEndpoints();
        return app;
    }

    // Refactored endpoints all live under the "api/v2/" route prefix (MapGroup("api/v2")); no legacy
    // endpoint does. That prefix is the sole discriminator that keeps the two document generations apart.
    internal static bool IsV2(ApiDescription api) =>
        api.RelativePath?.StartsWith(EndpointsExtensions.V2RoutePrefix + "/", StringComparison.OrdinalIgnoreCase) == true;

    // v2 documents (v2-User, v2-ProviderAdmin, v2-Admin): role-scoped *documentation* only. Membership is
    // decided statically by the endpoint's ApiExplorer GroupName (set via .WithGroupName(ApiAudience.*));
    // an untagged v2 endpoint defaults to the User document. This is purely a description grouping — it is
    // NOT authorization. The Gateway enforces RBAC at call time, independently of these documents, so the
    // spec stays complete and viewable without auth and without any database lookup.
    private static string AudienceOf(ApiDescription api) =>
        string.IsNullOrEmpty(api.GroupName) ? ApiAudience.User : api.GroupName;

    private static Action<OpenApiOptions> ConfigureV2OpenApi(string audience) => options =>
    {
        options.ShouldInclude = api => IsV2(api) && string.Equals(AudienceOf(api), audience, StringComparison.OrdinalIgnoreCase);
        HostInfrastructureExtensions.ConfigureSharedOpenApi(options);

        // Qualify each schema $ref id by its declaring endpoint so nested `record Request/Response/Body`
        // types no longer collide onto a single shared component schema (ADR-0062). Applied to v2 documents
        // ONLY (after ConfigureSharedOpenApi); the legacy v1 documents deliberately keep the framework default
        // id so they stay byte-stable. Top-level types keep the framework default; nested DTOs get prefixed.
        options.CreateSchemaReferenceId = jsonTypeInfo =>
            SchemaReferenceIdFor(jsonTypeInfo.Type) ?? OpenApiOptions.CreateDefaultSchemaReferenceId(jsonTypeInfo);

        // CyclopsUsage has no v2 route (the Gateway serves it); inject it into the User document so it stays
        // discoverable in Scalar once v1 is retired. Document-only — see CyclopsUsageDocumentTransformer.
        if (string.Equals(audience, ApiAudience.User, StringComparison.OrdinalIgnoreCase))
            options.AddDocumentTransformer<CyclopsUsageDocumentTransformer>();
    };

    // Single source of truth for ADR-0062/0063's declaring-endpoint qualification (also called directly by the
    // reflection guardrail test). Returns null for top-level types, deferring to the framework default at the
    // call site above — this method owns only the nested-type prefixing decision, the part that can collide.
    internal static string? SchemaReferenceIdFor(Type type)
    {
        if (type.DeclaringType is not { } declaring)
        {
            return null;
        }

        var prefix = declaring.Name.EndsWith("Endpoint", StringComparison.Ordinal)
            ? declaring.Name[..^"Endpoint".Length]
            : declaring.Name;
        return prefix + type.Name;
    }
}