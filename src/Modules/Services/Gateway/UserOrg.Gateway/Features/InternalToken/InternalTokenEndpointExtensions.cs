namespace UserOrg.Gateway.Features.InternalToken;

/// <summary>
/// Extension methods for mapping Internal Token feature endpoints.
/// Automatically registers JWKS endpoint for token validation.
/// </summary>
internal static class InternalTokenEndpointExtensions
{
    extension(WebApplication app)
    {
        /// <summary>
        /// Maps Internal Token feature endpoints to the application.
        /// Automatically registers the JWKS endpoint for public key exposure.
        /// </summary>
        /// <param name="app">The WebApplication to map endpoints to</param>
        /// <returns>The WebApplication for method chaining</returns>
        public WebApplication MapInternalTokenEndpoints()
        {
            ArgumentNullException.ThrowIfNull(app);

            app.MapGet("/.well-known/openid-configuration", GetOidcConfigurationEndpoint)
                .WithName("OidcDiscovery")
                .WithSummary("OpenID Connect Discovery")
                .WithDescription("Returns the OpenID Connect configuration metadata");

            app.MapGet("/.well-known/jwks.json", GetJwksEndpoint)
                .WithName("GetJwks")
                .Produces<string>(contentType: "application/json")
                .WithSummary("Get JSON Web Key Set")
                .WithDescription("Returns the public keys used for verifying JWT tokens");

            return app;
        }
    }

    private static IResult GetOidcConfigurationEndpoint(HttpContext context, Microsoft.Extensions.Options.IOptions<InternalTokenOptions> options)
    {
        // Prefer configured issuer to avoid dependency on forwarded host/scheme. Fallback to request values when not set.
        var issuer = options?.Value?.Issuer?.TrimEnd('/') ?? $"{context.Request.Scheme}://{context.Request.Host}";
        var config = new
        {
            issuer = issuer,
            jwks_uri = $"{issuer}/.well-known/jwks.json",
            id_token_signing_alg_values_supported = new[] { "RS256" }
        };
        return Results.Ok(config);
    }

    private static async Task<IResult> GetJwksEndpoint(IJwksProvider jwksProvider)
    {
        var json = await jwksProvider.GetCachedJwksJsonAsync();
        return Results.Content(json, "application/json");
    }
}