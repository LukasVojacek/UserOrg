using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.AspNetCore.Http;

using UserOrg.Shared;

namespace Core.UnitTests.Shared;

/// <summary>
/// Characterization tests (issue #253 audit risky spot 1) pinning TODAY's behaviour of
/// <see cref="Caller.BindAsync"/> / <see cref="Caller.ResolveIdpId"/> before the planned change
/// (ADR-0068 C1): reading <c>preferred_username</c> for the user name and normalizing an
/// <c>@</c>-suffixed <c>sub</c> before <see cref="IdpId.From"/>. These tests lock the current,
/// un-normalized, <c>Name</c>-claim-based behaviour so the planned change shows up as an expected
/// red, not a silent regression.
/// </summary>
public sealed class CallerBindingTests
{
    private static DefaultHttpContext ContextWithClaims(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) };

    [Fact]
    public void Should_ResolveIdpId_FromSubClaim_When_Present()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "abc-123"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(new IdpId("abc-123"), idpId);
    }

    [Fact]
    public void Should_ResolveIdpId_FromNameIdentifierClaim_When_SubMissing()
    {
        var context = ContextWithClaims(new Claim(ClaimTypes.NameIdentifier, "fallback-id"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(new IdpId("fallback-id"), idpId);
    }

    [Fact]
    public void Should_ReturnDefault_When_NoSubOrNameIdentifierClaim()
    {
        var context = ContextWithClaims();

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(default, idpId);
    }

    [Fact]
    public void Should_NormalizeSub_TruncatingAtFirstAtSign_When_SubContainsAtSuffix()
    {
        // ADR-0068 C1: sub.Split('@')[0] is applied before IdpId.From, matching how v1 stores
        // IdentityProviderUserId (UserInitializationEndpointFilter.cs:36) so v2 keys on the same
        // normalized identity instead of provisioning a duplicate user for an @-suffixed sub.
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "cc921a1d@eudat.eu"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(new IdpId("cc921a1d"), idpId);
    }

    [Fact]
    public void Should_LeaveSubUnchanged_When_SubHasNoAtSuffix()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "cc921a1d"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(new IdpId("cc921a1d"), idpId);
    }

    [Fact]
    public void Should_ReturnDefault_When_SubNormalizesToEmptyStringBeforeAtSign()
    {
        // "@eudat.eu".Split('@')[0] == "" — must not throw IdpId.Create's "cannot be empty".
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "@eudat.eu"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(default, idpId);
    }

    [Fact]
    public void Should_ReturnDefault_When_SubIsShorterThanMinimumLength()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "ab"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(default, idpId);
    }

    [Fact]
    public void Should_ReturnDefault_When_SubIsOnlyAtSign()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "@"));

        var idpId = Caller.ResolveIdpId(context);

        Assert.Equal(default, idpId);
    }

    [Fact]
    public async Task Should_BindRole_FromClaimTypesRole_When_Present()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(ClaimTypes.Role, "Owner"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal("Owner", caller!.Role);
    }

    [Fact]
    public async Task Should_BindRoleAsEmptyString_When_RoleClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal(string.Empty, caller!.Role);
    }

    [Fact]
    public async Task Should_BindName_FromJwtNameClaim_When_Present()
    {
        // TODAY: BindAsync reads JwtRegisteredClaimNames.Name, not preferred_username (planned C1 change).
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.Name, "Jane Doe"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal("Jane Doe", caller!.Name);
    }

    [Fact]
    public async Task Should_BindNameAsNull_When_NameClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.Name);
    }

    [Fact]
    public async Task Should_BindNameAsNull_When_OnlyPreferredUsernameClaimPresent()
    {
        // TODAY: preferred_username is not read at all — Name stays null even when it's present.
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "jane.doe"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.Name);
    }

    [Fact]
    public async Task Should_BindEmail_FromEmailClaim_When_Present()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.Email, "jane.doe@example.com"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal(Email.Create("jane.doe@example.com"), caller!.Email);
    }

    [Fact]
    public async Task Should_BindEmail_FromClaimTypesEmail_When_ShortEmailClaimRemapped()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(ClaimTypes.Email, "jane.doe@example.com"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal(Email.Create("jane.doe@example.com"), caller!.Email);
    }

    [Fact]
    public async Task Should_BindEmailAsNull_When_EmailClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.Email);
    }

    [Fact]
    public async Task Should_BindUserName_FromPreferredUsernameClaim_When_Present()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "jane.doe"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal(UserName.Create("jane.doe"), caller!.UserName);
    }

    [Fact]
    public async Task Should_BindUserNameAsNull_When_PreferredUsernameClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.UserName);
    }

    [Fact]
    public async Task Should_BindUserNameAsNull_When_PreferredUsernameIsTooShort()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, "jd"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.UserName);
    }

    [Fact]
    public async Task Should_BindUserNameAsNull_When_PreferredUsernameExceedsMaxLength()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.PreferredUsername, new string('a', UserName.MaxLenght + 1)));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.UserName);
    }

    [Fact]
    public async Task Should_BindEmailAsNull_When_EmailClaimIsMalformed()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(JwtRegisteredClaimNames.Email, "not-an-email"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.Email);
    }

    [Fact]
    public async Task Should_BindProjectRoles_FromWellFormedJsonClaim()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("projectRoles", "{\"Owner\":[\"PROJ-1\"]}"));

        var caller = await Caller.BindAsync(context);

        Assert.True(caller!.ProjectRoles.ContainsKey("Owner"));
        Assert.Contains(ProjectShortName.Create("PROJ-1"), caller.ProjectRoles["Owner"]);
    }

    [Fact]
    public async Task Should_BindEmptyProjectRoles_When_ClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Empty(caller!.ProjectRoles);
    }

    [Fact]
    public async Task Should_BindEmptyProjectRoles_When_ClaimIsMalformedJson()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("projectRoles", "{not-json"));

        var caller = await Caller.BindAsync(context);

        Assert.Empty(caller!.ProjectRoles);
    }

    [Fact]
    public async Task Should_BindEmptyRoleSet_When_ProjectRolesRoleValueIsJsonNull()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("projectRoles", "{\"admin\":null}"));

        var caller = await Caller.BindAsync(context);

        Assert.Empty(caller!.ProjectRoles["admin"]);
        Assert.Empty(caller.Scope);
    }

    [Fact]
    public async Task Should_BindOtherRoles_When_OneProjectRolesRoleValueIsJsonNull()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim("projectRoles", "{\"admin\":null,\"user\":[\"p1\"]}"));

        var caller = await Caller.BindAsync(context);

        Assert.Empty(caller!.ProjectRoles["admin"]);
        Assert.Contains(ProjectShortName.Create("p1"), caller.ProjectRoles["user"]);
    }

    [Fact]
    public async Task Should_BindProviderName_FromClaim_When_Present()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(AuthConstants.ProviderNameClaimType, "vsb"));

        var caller = await Caller.BindAsync(context);

        Assert.NotNull(caller!.ProviderName);
        Assert.Equal(Core.Domain.Provider.ProviderName.From("vsb"), caller.ProviderName);
    }

    [Fact]
    public async Task Should_BindProviderNameAsNull_When_ClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.ProviderName);
    }

    [Fact]
    public async Task Should_BindProviderNameAsNull_When_ClaimExceedsMaxLength()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(AuthConstants.ProviderNameClaimType, new string('a', 101)));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.ProviderName);
    }

    [Fact]
    public async Task Should_ReportServiceKeyIdentity_When_AmClaimIsApiKey()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey)));

        var caller = await Caller.BindAsync(context);

        Assert.True(caller!.IsServiceKeyIdentity);
    }

    [Fact]
    public async Task Should_ReportInteractiveIdentity_When_AmClaimIsJwtBearerToken()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.JwtBearerToken)));

        var caller = await Caller.BindAsync(context);

        Assert.False(caller!.IsServiceKeyIdentity);
    }

    [Fact]
    public async Task Should_ReportInteractiveIdentity_When_AmClaimMissing()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "user-1"));

        var caller = await Caller.BindAsync(context);

        Assert.False(caller!.IsServiceKeyIdentity);
    }

    [Fact]
    public async Task Should_ReportInteractiveIdentity_When_AmClaimMalformed_NotThrow()
    {
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(EAuthorizationMethod.ClaimType, "not-a-real-method"));

        // A FromString-based read would throw here and fail this test - that is the point of the case.
        var caller = await Caller.BindAsync(context);

        Assert.False(caller!.IsServiceKeyIdentity);
    }

    [Fact]
    public async Task Should_ReportInteractiveIdentity_When_AmClaimIsApiKeyWithWrongCasing()
    {
        // Pins the comparison as ordinal exact-match, not OrdinalIgnoreCase: no writer in src/ emits
        // a differently-cased "am" value, so a lowercase claim must not resolve to a service key.
        var context = ContextWithClaims(
            new Claim(JwtRegisteredClaimNames.Sub, "user-1"),
            new Claim(EAuthorizationMethod.ClaimType, "apikey"));

        var caller = await Caller.BindAsync(context);

        Assert.False(caller!.IsServiceKeyIdentity);
    }

    [Fact]
    public async Task Should_BindRawSub_FromUnnormalizedSubClaim_When_SubHasAtSuffix()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "cc921a1d@eudat.eu"));

        var caller = await Caller.BindAsync(context);

        Assert.Equal("cc921a1d@eudat.eu", caller!.RawSub);
    }

    [Fact]
    public async Task Should_BindRawSubAsNull_When_IdpIdResolvedFromNameIdentifierFallback()
    {
        var context = ContextWithClaims(new Claim(ClaimTypes.NameIdentifier, "fallback-id"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller!.RawSub);
    }

    [Fact]
    public async Task Should_ReturnNull_When_SubAndNameIdentifierBothMissing()
    {
        var context = ContextWithClaims();

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }

    // ── BindAsync — malformed sub must never throw (issue #253) ─────────

    [Fact]
    public async Task Should_ReturnNull_When_SubNormalizesToEmptyStringBeforeAtSign()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "@eudat.eu"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }

    [Fact]
    public async Task Should_ReturnNull_When_SubIsShorterThanMinimumLength()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "ab"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }

    [Fact]
    public async Task Should_ReturnNull_When_SubIsOnlyAtSign()
    {
        var context = ContextWithClaims(new Claim(JwtRegisteredClaimNames.Sub, "@"));

        var caller = await Caller.BindAsync(context);

        Assert.Null(caller);
    }
}
