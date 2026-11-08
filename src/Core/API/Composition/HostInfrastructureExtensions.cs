using System.Diagnostics;
using System.Text.Json.Serialization;

using AspNetCoreRateLimit;

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.FeatureManagement;
using Microsoft.OpenApi;

using UserOrg.ServiceDefaults;

using UserOrganizationManagement.API;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.OpenApi;
using UserOrganizationManagement.Domain.Configuration;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Configurations;

namespace UserOrganizationManagement.API.Composition;

internal static class HostInfrastructureExtensions
{
    internal const string DefaultCorsName = "UserOrgDefaultOrigins";

    internal static WebApplicationBuilder AddHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddTransient<DefaultUserAgentHandler>();

        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddHybridCache(options =>
        {
            // The HEAppE cluster catalog cached by the TemplatesInfo path (Heappe_Cluster*_{identifier}) serializes
            // above the 1 MB default; HybridCache then logs an error and silently serves it uncached on every call.
            options.MaximumPayloadBytes = 10 * 1024 * 1024;
            options.DefaultEntryOptions = new HybridCacheEntryOptions
            {
                LocalCacheExpiration = TimeSpan.FromMinutes(30)
            };
        });

        builder.Services.ConfigureHttpClientDefaults(builder =>
        {
            builder.AddHttpMessageHandler<DefaultUserAgentHandler>();
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOptions<IpRateLimitOptions>().BindConfiguration("IpRateLimiting")
            .PostConfigure(options =>
            {
                options.RealIpHeader = string.Empty;
                options.ClientIdHeader = string.Empty;
                options.ClientWhitelist = [];
            });
        builder.Services.AddOptions<IpRateLimitPolicies>().BindConfiguration("IpRateLimitPolicies");

        builder.Services.AddOptions<ServiceAcesKeysConfiguration>().BindConfiguration(ServiceAcesKeysConfiguration.ConfigurationPath);
        builder.Services.AddOptions<ActionLink>().BindConfiguration(ActionLink.ConfigurationPath);
        builder.Services.AddOptions<MailConfiguration>().BindConfiguration(MailConfiguration.ConfigurationPath);

        builder.Services.AddInMemoryRateLimiting();
        builder.Services.AddMemoryCache();

        builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
        builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
        builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();

        builder.Services.AddOptionsWithValidateOnStart<ScalarUiOptions>()
            .BindConfiguration(ScalarUiOptions.SectionName)
            .Validate(options => string.IsNullOrWhiteSpace(options.Title) is false, $"{ScalarUiOptions.SectionName}:Title cannot be empty.")
            .Validate(options => string.IsNullOrWhiteSpace(options.RoutePrefix) is false, $"{ScalarUiOptions.SectionName}:RoutePrefix cannot be empty.");
        builder.AddScalarOidcLogin();
        builder.Services.AddOptions<DataAccessOptions>().BindConfiguration("DataAccessConfiguration");

        builder.Services.AddFeatureManagement();

        // Program.cs calls app.UseOutputCache() unconditionally, but the named policies are
        // registered by AddCore(), which is gated behind FeatureManagement:V2Api. This base
        // registration keeps the middleware resolvable with the flag off; AddCore() still adds
        // its policies on top when the flag is on.
        builder.Services.AddOutputCache();

        builder.AddOpenIdAuthServices();

        var isReadOnly = builder.Configuration.GetValue<bool>("FeatureManagement:ReadOnlyMode");
        if (!isReadOnly)
        {
            builder.AddEmailSenderClient();
        }

        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.SerializerOptions.PropertyNamingPolicy = null;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
            // Register PermissionKey converter globally so API model binding/serialization respects value object
            options.SerializerOptions.Converters.Add(new UserOrg.Shared.PermissionKeyJsonConverter());
        });

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(name: DefaultCorsName, builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    // Pagination + revalidation metadata live in response headers (ADR-0020 amendment / ADR-0023);
                    // AllowAnyHeader covers request headers only, so expose these so browser clients can read them.
                    .WithExposedHeaders("Link", "X-Total-Count", "ETag");
            });
        });

        // Register exception handler. Echo the active correlation id into every ProblemDetails body so a client
        // failure can be tied to the server logs (ADR-0020); the same id is already on the response header.
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["requestId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.AddServiceDefaults();

        return builder;
    }

    // Shared OpenAPI configuration applied to every document (legacy and v2 alike): feature-flag
    // transformers, API info, bearer auth, and the value-object schema mappings that preserve the
    // previous MapType<T> Swashbuckle behaviour.
    internal static void ConfigureSharedOpenApi(Microsoft.AspNetCore.OpenApi.OpenApiOptions options)
    {
        // Order matters: the documentation delegates registered via WithOpenApiDoc(...) assign
        // OpenApiOperation.Description outright, while FeatureFlagOperationTransformer prefixes
        // "[DISABLED] " onto it and FeatureFlagDocumentTransformer removes operations by matching that
        // prefix. Running the documentation delegates first keeps the marker on top, so a flag-disabled
        // endpoint is still dropped from the spec.
        options.AddOperationTransformer<OpenApiDocOperationTransformer>();
        options.AddOperationTransformer<FeatureFlagOperationTransformer>();
        options.AddDocumentTransformer<ApiInfoDocumentTransformer>();
        // Runs before DocumentPathGroupFilter and the feature-flag removals so operationIds are derived
        // from the complete, unfiltered operation set and stay identical across the split documents.
        options.AddDocumentTransformer<OperationIdDocumentTransformer>();
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
        options.AddDocumentTransformer<OidcSecuritySchemeTransformer>();
        options.AddDocumentTransformer<FeatureFlagDocumentTransformer>();
        options.AddDocumentTransformer<FeatureFlagCleanupTransformer>();
        options.AddSchemaTransformer<PascalCasingSchemaTransformer>();
        options.AddSchemaTransformer<NullableEnumSchemaTransformer>();

        // NOTE: schema $ref id qualification (CreateSchemaReferenceId, ADR-0062) is deliberately NOT set here.
        // It applies to v2 documents ONLY (registered in ConfigureV2OpenApi), so the legacy v1 documents stay
        // on the framework default id and remain byte-stable.

        // Type mappings: preserve previous MapType<T> Swashbuckle behaviour
        options.AddSchemaTransformer((schema, ctx, ct) =>
        {
            if (ctx.JsonTypeInfo.Type == typeof(ProjectId))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "uuid";
            }
            else if (ctx.JsonTypeInfo.Type == typeof(ProjectShortName))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = "string";
            }

            return Task.CompletedTask;
        });
    }
}