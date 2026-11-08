using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

using Microsoft.AspNetCore.Http;

using UserOrg.Shared;

namespace Core.Shared.Auth;

/// <summary>
/// The authenticated principal behind a request, resolved from the Gateway-issued internal token's
/// claims: its <see cref="IdpId"/> (<c>sub</c>), effective <see cref="Role"/>, optional display
/// <see cref="Name"/>, <see cref="Email"/>, <see cref="UserName"/>, and <see cref="ProjectRoles"/>
/// (role name → project short-names). Core trusts these claims as the source of truth for caller
/// identity and scope (ADR-0006) instead of re-deriving them from the database or the role service.
/// </summary>
/// <remarks>
/// Bound as an endpoint parameter via <see cref="BindAsync(HttpContext)"/>. Endpoints behind the
/// authorized <c>v2</c> group always receive a non-null <see cref="Caller"/>; a request that reaches
/// binding without a <c>sub</c> claim yields a <c>400</c>.
///
/// <see cref="Email"/> and <see cref="UserName"/> are nullable because service-to-service or API-key
/// tokens carry neither claim.
/// </remarks>
internal sealed record Caller(
    IdpId IdpId,
    string Role,
    string? Name,
    IReadOnlyDictionary<string, IReadOnlySet<ProjectShortName>> ProjectRoles,
    ProviderName? ProviderName = null,
    /// <summary>The caller's email address from the <c>email</c> claim, or <c>null</c> for service-to-service/API-key tokens.</summary>
    Email? Email = null,
    /// <summary>
    /// The caller's username from the <c>preferred_username</c> claim, or <c>null</c> for service-to-service/API-key tokens.
    /// Normalized via <see cref="ResolveIdpId"/> to match v1 IdentityProviderUserId storage and Forge permission lookup (ADR-0068 §4).
    /// </summary>
    UserName? UserName = null,
    /// <summary>
    /// The raw, unnormalized <c>sub</c> claim, or <c>null</c> when <see cref="IdpId"/> was resolved from
    /// the <see cref="ClaimTypes.NameIdentifier"/> fallback. Kept alongside the normalized <see cref="IdpId"/>
    /// because <see cref="ResolveIdpId"/> collapses distinct subjects that differ only after '@'; that loss
    /// is irreversible once written to the append-only audit log, so the raw value rides along for audit use.
    ///
    /// It is written verbatim into non-erasable storage on the standing assumption that <c>sub</c> is an
    /// opaque <c>&lt;id&gt;@&lt;idp&gt;</c> subject, not personal data - the '@' it contains is the very
    /// separator <see cref="ResolveIdpId"/> splits on, so it is not a sign of an e-mail address. The value is
    /// deliberately stored whole and unchecked: preserving the complete subject is the point of the field, and
    /// stripping or rejecting parts of it would defeat it. Should an IdP ever be configured to issue an e-mail
    /// address as <c>sub</c>, that assumption breaks at the IdP, and the fix belongs there.
    /// </summary>
    string? RawSub = null,
    /// <summary>
    /// Whether this caller authenticated as a provider service key rather than an interactive (human/OIDC)
    /// caller, resolved fail-closed from the <c>am</c> claim by <see cref="ReadIsServiceKeyIdentity"/>: only
    /// <c>am == "ApiKey"</c> is a service key — <c>JwtBearerToken</c>, an absent claim, and a malformed one
    /// all resolve to <c>false</c> (interactive). It discriminates the caller's <em>read</em> reach by natural
    /// key (ADR-0094 D10, <c>ReadScope</c>), so a caller misclassified here would either narrow a legitimate
    /// service key's reads or widen a human's. It does <em>not</em> gate writes: ADR-0105 abandons D9, and
    /// <see cref="ProviderWriteAuthorization"/> decides on the Admin role or a <see cref="ProviderName"/>
    /// claim match alone. Never derive this from <see cref="Role"/> instead - a legacy
    /// API-key header stamps <c>LegacyProvider</c>, not <c>Provider</c>, so the role alone does not tell
    /// the two identity kinds apart.
    /// </summary>
    bool IsServiceKeyIdentity = false)
{
    private const string ProjectRolesClaim = "projectRoles";

    /// <summary>The set of projects the caller may see, flattened from <see cref="ProjectRoles"/>.</summary>
    public IReadOnlySet<ProjectShortName> Scope { get; } =
        ProjectRoles.SelectMany(r => r.Value).ToHashSet();

    /// <summary>
    /// Resolves the caller's <see cref="IdpId"/> from the request principal's <c>sub</c> (or
    /// <see cref="ClaimTypes.NameIdentifier"/>) claim, or <c>default</c> when the token carries neither.
    /// Normalizes the <c>sub</c> by taking the part before '@' to match how v1 stores IdentityProviderUserId
    /// (UserInitializationEndpointFilter.cs:36) and how Forge's permission lookup matches, preventing duplicate
    /// user provisioning in v2 (ADR-0068 §4). Lives in the application Auth layer so the <see cref="IdpId"/>
    /// value object stays free of HTTP.
    /// </summary>
    public static IdpId ResolveIdpId(HttpContext context)
    {
        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(sub))
            return default;

        return IdpId.TryParse(sub.Split('@')[0], null, out var idpId) ? idpId : default;
    }

    public static ValueTask<Caller?> BindAsync(HttpContext context)
    {
        var rawSub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        var idpId = ResolveIdpId(context);
        if (idpId == default)
            return ValueTask.FromResult<Caller?>(null);

        var role = context.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var name = context.User.FindFirstValue(JwtRegisteredClaimNames.Name);
        var projectRoles = ReadProjectRoles(context.User.FindFirstValue(ProjectRolesClaim));
        var providerName = ReadProviderName(context.User.FindFirstValue(AuthConstants.ProviderNameClaimType));
        var email = ReadEmail(context.User.FindFirstValue(JwtRegisteredClaimNames.Email)
                              ?? context.User.FindFirstValue(ClaimTypes.Email));
        var userName = ReadUserName(context.User.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername));
        var isServiceKeyIdentity = ReadIsServiceKeyIdentity(context.User.FindFirstValue(EAuthorizationMethod.ClaimType));

        return ValueTask.FromResult<Caller?>(
            new Caller(idpId, role, name, projectRoles, providerName, email, userName, rawSub, isServiceKeyIdentity));
    }

    // why: only ApiKey is a service key - JwtBearerToken, an absent claim and a malformed one all resolve to interactive, which narrows the by-natural-key read reach in ReadScope (ADR-0094 D10) and does not gate writes at all (ADR-0105)
    // warn: never EAuthorizationMethod.FromString here - it throws on unknown/empty input, turning a malformed "am" claim into a 500 during model binding instead of that interactive result
    // why: the comparison is exact because every writer emits nameof/authMethod.ToString() (Gateway ApiKeyAuthenticationHandler.cs:78, TokenService.cs:80)
    private static bool ReadIsServiceKeyIdentity(string? claim) =>
        claim == nameof(EAuthorizationMethod.ApiKey);

    private static ProviderName? ReadProviderName(string? claim) =>
        Core.Domain.Provider.ProviderName.TryCreate(claim, out var providerName, out _) ? providerName : null;

    private static Email? ReadEmail(string? claim) =>
        Core.Shared.Email.TryParse(claim, null, out var email) ? email : null;

    private static UserName? ReadUserName(string? claim) =>
        Core.Domain.User.UserName.TryParse(claim, null, out var userName) ? userName : null;

    private static IReadOnlyDictionary<string, IReadOnlySet<ProjectShortName>> ReadProjectRoles(string? claim)
    {
        if (string.IsNullOrWhiteSpace(claim))
            return EmptyProjectRoles;

        Dictionary<string, string[]>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(claim);
        }
        catch (JsonException)
        {
            return EmptyProjectRoles;
        }

        if (raw is null)
            return EmptyProjectRoles;

        return raw.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlySet<ProjectShortName>)(kvp.Value ?? [])
                .Select(s => ProjectShortName.TryParse(s, null, out var sn) ? sn : (ProjectShortName?)null)
                .OfType<ProjectShortName>()
                .ToHashSet());
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<ProjectShortName>> EmptyProjectRoles =
        new Dictionary<string, IReadOnlySet<ProjectShortName>>();
}