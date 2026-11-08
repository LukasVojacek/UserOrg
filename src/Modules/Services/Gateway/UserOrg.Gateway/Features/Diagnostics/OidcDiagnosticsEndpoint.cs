using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace UserOrg.Gateway.Features.Diagnostics;

internal static class OidcDiagnosticsEndpoint
{
    private const string NotAvailable = "N/A";

    public static IEndpointRouteBuilder MapOidcDiagnostics(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_diagnostics/oidc", RenderReportAsync)
        .AllowAnonymous()
        .WithName("OidcDiagnostics")
        .WithDescription("Diagnostics endpoint for OIDC configuration");

        return endpoints;
    }

    private static async Task<IResult> RenderReportAsync(
        IOptionsMonitor<JwtBearerOptions> jwtOptions,
        IConfiguration configuration,
        ILogger<Program> logger)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== OIDC Configuration Diagnostics ===");
        sb.AppendLine();

        try
        {
            var options = jwtOptions.Get(Constants.BearerSchemeId);
            AppendBearerOptions(sb, options);
            var oidcConfig = await AppendDiscoveryDocumentAsync(sb, options);
            AppendValidationParameters(sb, options.TokenValidationParameters);
            AppendIntrospection(sb, configuration, options.Authority, oidcConfig);
        }
        catch (HttpRequestException ex)
        {
            AppendDiscoveryFailure(sb, ex);
            logger.LogError(ex, "Failed to retrieve OIDC configuration for diagnostics");
        }

        return Results.Text(sb.ToString(), "text/plain");
    }

    private static void AppendBearerOptions(StringBuilder sb, JwtBearerOptions options)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"Authority: {options.Authority}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"RequireHttpsMetadata: {options.RequireHttpsMetadata}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"SaveToken: {options.SaveToken}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"MapInboundClaims: {options.MapInboundClaims}");
        sb.AppendLine();
    }

    /// <summary>
    /// Fetches the discovery document through the options' configuration manager and writes its section;
    /// returns the document, or null when there is no manager or it yields no OpenID Connect configuration.
    /// </summary>
    private static async Task<OpenIdConnectConfiguration?> AppendDiscoveryDocumentAsync(StringBuilder sb, JwtBearerOptions options)
    {
        if (options.ConfigurationManager == null)
        {
            sb.AppendLine("? ConfigurationManager is null");
            return null;
        }

        sb.AppendLine("Attempting to retrieve OIDC configuration...");
        var config = await options.ConfigurationManager.GetConfigurationAsync(CancellationToken.None);
        if (config is not OpenIdConnectConfiguration oidcConfig)
        {
            return null;
        }

        AppendDocument(sb, oidcConfig);
        return oidcConfig;
    }

    private static void AppendDocument(StringBuilder sb, OpenIdConnectConfiguration oidcConfig)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"? OIDC Configuration retrieved successfully");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Issuer: {oidcConfig.Issuer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  JwksUri: {oidcConfig.JwksUri}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Authorization Endpoint: {oidcConfig.AuthorizationEndpoint}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Token Endpoint: {oidcConfig.TokenEndpoint}");
        sb.AppendLine();

        AppendSigningKeys(sb, oidcConfig.SigningKeys);

        sb.AppendLine();
        AppendList(sb, "Supported Response Types:", oidcConfig.ResponseTypesSupported);

        sb.AppendLine();
        AppendList(sb, "Supported Scopes:", oidcConfig.ScopesSupported);
    }

    private static void AppendSigningKeys(StringBuilder sb, ICollection<SecurityKey>? signingKeys)
    {
        if (signingKeys == null || signingKeys.Count == 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"? ERROR: No signing keys found!");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  This will cause signature validation to fail.");
            return;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"? Signing Keys: {signingKeys.Count} key(s) found");
        foreach (var key in signingKeys)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  - KeyId: {key.KeyId ?? NotAvailable}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    Type: {key.GetType().Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    CryptoProviderFactory: {key.CryptoProviderFactory?.GetType().Name ?? NotAvailable}");
        }
    }

    private static void AppendList(StringBuilder sb, string heading, IEnumerable<string>? items)
    {
        sb.AppendLine(heading);
        foreach (var item in items ?? [])
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  - {item}");
        }
    }

    private static void AppendValidationParameters(StringBuilder sb, TokenValidationParameters parameters)
    {
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"TokenValidationParameters:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidateIssuer: {parameters.ValidateIssuer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidIssuer: {parameters.ValidIssuer}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidateAudience: {parameters.ValidateAudience}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidAudience: {parameters.ValidAudience}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidateLifetime: {parameters.ValidateLifetime}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ValidateIssuerSigningKey: {parameters.ValidateIssuerSigningKey}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  ClockSkew: {parameters.ClockSkew}");
    }

    private static void AppendIntrospection(StringBuilder sb, IConfiguration configuration, string? authority, OpenIdConnectConfiguration? oidcConfig)
    {
        sb.AppendLine();
        sb.AppendLine("=== Token Introspection Configuration ===");

        var introspectionEnabled = ReadIntrospectionSetting(configuration, "Enabled");
        var isEnabled = string.IsNullOrWhiteSpace(introspectionEnabled) || bool.Parse(introspectionEnabled);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Enabled: {(isEnabled ? "✓ YES" : "✗ NO (bypassed)")}");
        if (!isEnabled)
        {
            return;
        }

        AppendIntrospectionCredentials(sb, ReadIntrospectionSetting(configuration, "ClientId"), ReadIntrospectionSetting(configuration, "ClientSecret"));
        AppendIntrospectionEndpoint(sb, ReadIntrospectionSetting(configuration, "Endpoint"), authority, oidcConfig);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Cache: Enabled (distributed memory cache)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  TTL Strategy: Based on token expiration (min 5s, max 5min, default 60s)");
    }

    /// <summary>
    /// Reads <c>Idp:Introspection:{name}</c> from configuration, falling back to the
    /// <c>Idp__Introspection__{name}</c> environment variable, as the introspection service does.
    /// </summary>
    private static string? ReadIntrospectionSetting(IConfiguration configuration, string name)
        => configuration[$"Idp:Introspection:{name}"] ?? Environment.GetEnvironmentVariable($"Idp__Introspection__{name}");

    private static void AppendIntrospectionCredentials(StringBuilder sb, string? clientId, string? clientSecret)
    {
        var hasClientId = !string.IsNullOrWhiteSpace(clientId);
        var hasClientSecret = !string.IsNullOrWhiteSpace(clientSecret);
        var authMode = (hasClientId && hasClientSecret) ? "Basic Auth" : "Anonymous";

        sb.AppendLine(CultureInfo.InvariantCulture, $"Authentication Mode: {authMode}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Client ID: {DescribeConfigured(hasClientId)}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Client Secret: {DescribeConfigured(hasClientSecret)}");
    }

    private static string DescribeConfigured(bool configured) => configured ? "✓ Configured" : "✗ Not configured";

    private static void AppendIntrospectionEndpoint(StringBuilder sb, string? explicitEndpoint, string? authority, OpenIdConnectConfiguration? oidcConfig)
    {
        if (!string.IsNullOrWhiteSpace(explicitEndpoint))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Endpoint: {explicitEndpoint} (explicit)");
            return;
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"Endpoint: Will be discovered from {authority}/.well-known/openid-configuration");
        sb.AppendLine(DescribeDiscoveredEndpoint(oidcConfig));
    }

    private static string DescribeDiscoveredEndpoint(OpenIdConnectConfiguration? oidcConfig)
    {
        if (oidcConfig is null)
        {
            return "  ⚠ Could not retrieve OIDC configuration to discover endpoint";
        }

        return oidcConfig.AdditionalData.TryGetValue("introspection_endpoint", out var introspectionEndpoint)
            ? string.Create(CultureInfo.InvariantCulture, $"  Discovered Endpoint: {introspectionEndpoint}")
            : "  ⚠ introspection_endpoint not found in discovery document";
    }

    private static void AppendDiscoveryFailure(StringBuilder sb, HttpRequestException ex)
    {
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"? ERROR retrieving OIDC configuration:");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Exception Type: {ex.GetType().Name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Message: {ex.Message}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  StackTrace: {ex.StackTrace}");

        if (ex.InnerException != null)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Inner Exception:");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    Type: {ex.InnerException.GetType().Name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    Message: {ex.InnerException.Message}");
        }
    }
}