using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;

using UserOrg.Shared;

namespace UserOrg.Gateway.Features.InternalToken;

internal sealed partial class TokenService
{
    // JwtSecurityTokenHandler is thread-safe for CreateToken/WriteToken; share a single instance.
    private static readonly JwtSecurityTokenHandler _tokenHandler = new();

    private readonly KeyService _keyService;
    private readonly InternalTokenOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(KeyService keyService, InternalTokenOptions options, ILogger<TokenService> logger)
    {
        _keyService = keyService ?? throw new ArgumentNullException(nameof(keyService));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    /// <summary>
    /// Generates a JSON Web Token (JWT) for the specified user with the provided claims.
    /// </summary>
    /// <param name="subject">The unique identifier of the user (subject claim).</param>
    /// <param name="username">The username of the user.</param>
    /// <param name="role">The primary role of the user.</param>
    /// <param name="email">Optional email address of the user.</param>
    /// <param name="projectRoles">Optional dictionary mapping project role names to collections of project short names.
    /// The dictionary key is the project role (for example "ProjectUser", "ProjectManager") and the value is a
    /// list of project short names in which the user has that role. Example:
    /// <code>{ "ProjectUser": ["projectA","projectC"], "ProjectManager": ["projectB"] }</code>
    /// Absence of a project means the user has no access to that project.</param>
    /// <param name="originalJti">The <c>jti</c> of the token that authenticated the caller, emitted verbatim as the
    /// <c>original_jti</c> claim; when <c>null</c> the claim is omitted. The token's own <c>jti</c> is unaffected.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the generated JWT as a string.</returns>
    /// <remarks>
    /// The generated token includes standard claims (sub, preferred_username, role, jti, iat, email) and optionally
    /// a custom "projectRoles" claim containing a JSON-serialized representation of project-specific roles. The
    /// JSON structure maps role names to arrays of project short names (key = projectRole, value = collection of
    /// project short names). The token is signed using the credentials provided by the <see cref="KeyService"/> and
    /// expires based on the configured expiration time in <see cref="InternalTokenOptions"/>.
    /// </remarks>
    public async Task<string?> GenerateTokenAsync(EAuthorizationMethod authMethod, string subject, string username, string providerId, string role, string? email, IDictionary<string, ICollection<string>>? projectRoles = null, string? providerName = null, string? originalJti = null)
    {
        if (!HasRequiredClaims(subject, username, role))
        {
            return null;
        }

        var issuer = _options.Issuer;
        var audience = _options.Audience;
        var expirationMinutes = _options.ExpirationMinutes;

        // Build claims as a dictionary so complex objects (like projectRoles) are serialized as JSON objects
        var claimsDict = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = subject,
            [JwtRegisteredClaimNames.PreferredUsername] = username,
            [ClaimTypes.Role] = role,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iat] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            [EAuthorizationMethod.ClaimType] = authMethod.ToString(),
            [AuthConstants.ProviderIdClaimType] = providerId
        };
        AddOptionalClaims(claimsDict, providerName, email, originalJti);
        AddProjectRoles(claimsDict, projectRoles);

        var signingCredentials = await _keyService.GetSigningCredentialsAsync();
        var kid = _keyService.GetCurrentKeyId();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            // Use Claims dictionary so non-string values are correctly serialized
            Claims = claimsDict,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            NotBefore = DateTime.UtcNow,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = _tokenHandler.WriteToken(token);

        LogJwtGenerated(_logger, kid, expirationMinutes);

        return tokenString;
    }

    /// <summary>
    /// Instead of throwing, logs a warning naming the first empty one of subject, username and role and
    /// returns false.
    /// </summary>
    private bool HasRequiredClaims(string subject, string username, string role)
    {
        if (string.IsNullOrEmpty(subject))
        {
            _logger.LogWarning("GenerateTokenAsync called with empty or null subject");
            return false;
        }

        if (string.IsNullOrEmpty(username))
        {
            _logger.LogWarning("GenerateTokenAsync called with empty or null username");
            return false;
        }

        if (string.IsNullOrEmpty(role))
        {
            _logger.LogWarning("GenerateTokenAsync called with empty or null role");
            return false;
        }

        return true;
    }

    private static void AddOptionalClaims(Dictionary<string, object> claimsDict, string? providerName, string? email, string? originalJti)
    {
        // Additive provider-name claim (ADR-0011) — present for provider service-key identities and,
        // since D5, an interactive ProviderAdmin who holds the role and a still-assigned provider.
        if (!string.IsNullOrEmpty(providerName))
        {
            claimsDict[AuthConstants.ProviderNameClaimType] = providerName;
        }

        if (!string.IsNullOrEmpty(email))
        {
            claimsDict[JwtRegisteredClaimNames.Email] = email;
        }

        if (originalJti is not null)
        {
            claimsDict[AuthConstants.OriginalJtiClaimType] = originalJti;
        }
    }

    private static void AddProjectRoles(Dictionary<string, object> claimsDict, IDictionary<string, ICollection<string>>? projectRoles)
    {
        if (projectRoles != null && projectRoles.Count > 0)
        {
            // Convert to a plain dictionary of string -> string[] so the JWT handler emits a JSON object/array
            claimsDict["projectRoles"] = projectRoles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToArray() ?? Array.Empty<string>());
        }
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        try
        {
            var issuer = _options.Issuer;
            var audience = _options.Audience;

            var signingCredentials = await _keyService.GetSigningCredentialsAsync();

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = signingCredentials.Key,
                ClockSkew = TimeSpan.FromSeconds(60)
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    /// <summary>
    /// Generates a service-to-service JWT token containing the origin_service claim.
    /// </summary>
    /// <param name="originService">The name of the calling service (e.g., "userorg", "gateway")</param>
    /// <param name="targetAudience">The target audience for the token (e.g., "forge")</param>
    /// <returns>A JWT token string suitable for S2S authentication</returns>
    public async Task<string?> GenerateServiceToServiceTokenAsync(string originService, string targetAudience)
    {
        if (string.IsNullOrEmpty(originService))
        {
            _logger.LogWarning("GenerateServiceToServiceTokenAsync called with empty or null originService");
            return null;
        }

        if (string.IsNullOrEmpty(targetAudience))
        {
            _logger.LogWarning("GenerateServiceToServiceTokenAsync called with empty or null targetAudience");
            return null;
        }

        var issuer = _options.Issuer;
        var expirationMinutes = _options.ExpirationMinutes;

        var claimsDict = new Dictionary<string, object>
        {
            ["origin_service"] = originService,
            [JwtRegisteredClaimNames.Sub] = $"service:{originService}",
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            [JwtRegisteredClaimNames.Iat] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var signingCredentials = await _keyService.GetSigningCredentialsAsync();
        var kid = _keyService.GetCurrentKeyId();

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Claims = claimsDict,
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            NotBefore = DateTime.UtcNow,
            Issuer = issuer,
            Audience = targetAudience,
            SigningCredentials = signingCredentials
        };

        var token = _tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = _tokenHandler.WriteToken(token);

        _logger.LogDebug(
            "Generated S2S JWT for service {OriginService} targeting {Audience} with kid {Kid}, expires in {Minutes} minutes",
            originService,
            targetAudience,
            kid,
            expirationMinutes);

        return tokenString;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Generated JWT with kid {Kid}, expires in {Minutes} minutes")]
    private static partial void LogJwtGenerated(ILogger logger, string kid, int minutes);
}