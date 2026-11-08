using DomainProviderName = Core.Domain.Provider.ProviderName;

namespace Core.UnitTests.Providers;

/// <summary>
/// The decided write model (ADR-0105): a caller whose role is <c>Admin</c> may write any provider, and every
/// other caller may write only the provider its <c>provider_name</c> claim names. The authentication method —
/// <see cref="Caller.IsServiceKeyIdentity"/> — does not enter the write decision at all, so an interactive
/// <c>ProviderAdmin</c> and a provider service key on the same claim decide identically.
/// </summary>
public sealed class ProviderWriteAuthorizationTests
{
    private static readonly WellKnownRoles Roles = new();

    private static Caller MakeCaller(string role, bool isServiceKeyIdentity, string? providerName = null) =>
        new(
            new IdpId("u"),
            role,
            null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            providerName is null ? null : DomainProviderName.From(providerName),
            IsServiceKeyIdentity: isServiceKeyIdentity);

    [Fact]
    public void Should_AllowAdmin_For_AnyProvider()
    {
        var caller = MakeCaller(Roles.DefaultAdminRoleName, isServiceKeyIdentity: false);

        Assert.True(ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("acme"), Roles));
    }

    /// <summary>
    /// Admin's branch never reads the target, so a <c>provider_name</c> claim it happens to carry must not
    /// narrow it: the caller below is claimed onto <c>acme</c> while the rows target everything else.
    /// </summary>
    public static IEnumerable<object[]> ProvidersAdminMayWrite()
    {
        yield return ["acme"];
        yield return ["globex"];
        yield return ["ACME"];
        yield return ["other"];
    }

    [Theory]
    [MemberData(nameof(ProvidersAdminMayWrite))]
    public void Should_AllowAdmin_ForEveryProvider_RegardlessOfItsOwnProviderClaim(string target)
    {
        var caller = MakeCaller(Roles.DefaultAdminRoleName, isServiceKeyIdentity: false, "acme");

        Assert.True(
            ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From(target), Roles),
            $"Admin must be allowed to write provider '{target}'.");
    }

    /// <summary>
    /// The role comparison is case-insensitive, and nothing else in this suite pins that: every other admin
    /// case passes <see cref="WellKnownRoles.DefaultAdminRoleName"/> verbatim, so replacing the gate's
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> with <see cref="StringComparison.Ordinal"/> would
    /// leave all of them green. The role reaches this service as a claim minted from an external identity
    /// provider's data, whose casing it does not control, and <c>ReadScope</c> leans on the same
    /// insensitivity for the read tiers.
    /// </summary>
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("admin")]
    [InlineData("aDmIn")]
    public void Should_AllowAdmin_RegardlessOfRoleClaimCasing(string role)
    {
        var caller = MakeCaller(role, isServiceKeyIdentity: false);

        Assert.True(
            ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("globex"), Roles),
            $"Role '{role}' must be recognised as Admin and allowed to write a foreign provider.");
    }

    [Fact]
    public void Should_AllowProviderIdentity_For_OwnProvider_RegardlessOfCasing()
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "AcmeHPC");

        Assert.True(ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("acmehpc"), Roles));
    }

    [Fact]
    public void Should_RejectProviderIdentity_For_OtherProvider()
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "acme");

        Assert.False(ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("other"), Roles));
    }

    [Fact]
    public void Should_RejectNonAdmin_Without_ProviderClaim()
    {
        var caller = MakeCaller("User", isServiceKeyIdentity: false);

        Assert.False(ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("acme"), Roles));
    }

    [Fact]
    public void Should_AllowAdmin_For_MalformedRawName()
    {
        var caller = MakeCaller(Roles.DefaultAdminRoleName, isServiceKeyIdentity: false);

        Assert.True(ProviderWriteAuthorization.Authorize(caller, "   ", Roles));
    }

    [Fact]
    public void Should_RejectNonAdmin_For_MalformedRawName()
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "acme");

        Assert.False(ProviderWriteAuthorization.Authorize(caller, "   ", Roles));
    }

    [Fact]
    public void Should_AllowProviderIdentity_For_OwnProvider_FromCasedAndPaddedRawName()
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "AcmeHPC");

        Assert.True(ProviderWriteAuthorization.Authorize(caller, "  acmehpc  ", Roles));
    }

    [Fact]
    public void Should_RejectProviderIdentity_For_OtherProvider_FromRawName()
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "acme");

        Assert.False(ProviderWriteAuthorization.Authorize(caller, "other", Roles));
    }

    /// <summary>
    /// The legacy <c>X-Api-ProviderServiceKey</c> header stamps role <c>LegacyProvider</c>
    /// (<c>ApiKeyAuthenticationHandler.cs:150-157</c>), which matches no well-known role name, so this caller
    /// clears the gate on its claim alone — the way every non-<c>Admin</c> role does (ADR-0105 rule 2).
    /// </summary>
    [Fact]
    public void Should_Allow_When_CallerIsLegacyProviderServiceKey()
    {
        var caller = MakeCaller("LegacyProvider", isServiceKeyIdentity: true, "acme");

        Assert.True(ProviderWriteAuthorization.Authorize(caller, "acme", Roles));
    }

    /// <summary>
    /// An absent <c>am</c> claim resolves to <see cref="Caller.IsServiceKeyIdentity"/> <c>false</c> in
    /// <c>Caller.BindAsync</c>. Under ADR-0105 the claim, not the authentication method, decides, so this
    /// caller is allowed with no role at all and no <c>am</c> claim to read — the D9 pin this replaces
    /// asserted the opposite.
    /// </summary>
    [Fact]
    public void Should_Allow_When_AmClaimAbsent_And_ProviderClaimMatches()
    {
        var caller = MakeCaller(role: string.Empty, isServiceKeyIdentity: false, "acme");

        Assert.True(ProviderWriteAuthorization.Authorize(caller, "acme", Roles));
    }

    /// <summary>
    /// The 15 provider-write call sites split into two groups: most authorize the raw route-parameter string
    /// before parsing it (ADR-0020 ordering, "Authorize-before-validate ordering (#261b)"), while the Resource
    /// CRUD group authorizes an already-parsed <c>ProviderName</c>. Both overloads share the same ownership
    /// check, so covering both here proves the ownership check reaches every call site.
    /// </summary>
    public static IEnumerable<object[]> WriteShapes()
    {
        yield return ["provider update", false];
        yield return ["location create/update/remove", false];
        yield return ["resource CRUD", true];
        yield return ["assigned-resource assignment", false];
    }

    [Theory]
    [MemberData(nameof(WriteShapes))]
    public void Should_Allow_When_CallerIsInteractiveProviderAdmin_ForOwnProvider(string writeShape, bool targetIsParsedProviderName)
    {
        var caller = MakeCaller(Roles.DefaultProviderAdminRoleName, isServiceKeyIdentity: false, "acme");

        var authorized = targetIsParsedProviderName
            ? ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("acme"), Roles)
            : ProviderWriteAuthorization.Authorize(caller, "acme", Roles);

        Assert.True(authorized, $"{writeShape}: a ProviderAdmin claimed onto 'acme' must be allowed to write 'acme'.");
    }

    /// <summary>
    /// The regression this file guards: <c>ProviderAdmin</c> is a tenant admin, so its role alone must not
    /// clear the gate for a provider its claim does not name (ADR-0105; the live cross-tenant write
    /// reproduced on 2026-09-12).
    /// </summary>
    [Theory]
    [MemberData(nameof(WriteShapes))]
    public void Should_Deny_When_CallerIsInteractiveProviderAdmin_ForForeignProvider(string writeShape, bool targetIsParsedProviderName)
    {
        var caller = MakeCaller(Roles.DefaultProviderAdminRoleName, isServiceKeyIdentity: false, "acme");

        var authorized = targetIsParsedProviderName
            ? ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("globex"), Roles)
            : ProviderWriteAuthorization.Authorize(caller, "globex", Roles);

        Assert.False(authorized, $"{writeShape}: a ProviderAdmin claimed onto 'acme' must not be allowed to write 'globex'.");
    }

    /// <summary>
    /// <c>ProviderName</c> keeps the casing it is given and only trims (<c>ProviderName.cs:36-40</c>); the
    /// case-insensitivity lives in its <c>Equals</c>/<c>GetHashCode</c>. The gate therefore has to compare the
    /// value objects: the <c>owned.Value == target.Value</c> string comparison ADR-0012:31 forbids fails
    /// exactly this case.
    /// </summary>
    [Theory]
    [MemberData(nameof(WriteShapes))]
    public void Should_AllowProviderAdmin_For_OwnProvider_RegardlessOfCasing(string writeShape, bool targetIsParsedProviderName)
    {
        var caller = MakeCaller(Roles.DefaultProviderAdminRoleName, isServiceKeyIdentity: false, "acme");

        var authorized = targetIsParsedProviderName
            ? ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("ACME"), Roles)
            : ProviderWriteAuthorization.Authorize(caller, "  ACME  ", Roles);

        Assert.True(authorized, $"{writeShape}: a ProviderAdmin claimed onto 'acme' must be allowed to write 'ACME'.");
    }

    [Theory]
    [MemberData(nameof(WriteShapes))]
    public void Should_Allow_When_CallerIsProviderServiceKey(string writeShape, bool targetIsParsedProviderName)
    {
        var caller = MakeCaller("Provider", isServiceKeyIdentity: true, "acme");

        var authorized = targetIsParsedProviderName
            ? ProviderWriteAuthorization.Authorize(caller, DomainProviderName.From("acme"), Roles)
            : ProviderWriteAuthorization.Authorize(caller, "acme", Roles);

        Assert.True(authorized, writeShape);
    }

    /// <summary>
    /// Two login paths onto one actor: role and claim held fixed, only the authentication method differs.
    /// Both roles matter — <c>ProviderAdmin</c> is the interactive tenant admin and <c>Provider</c> the role a
    /// service key is stamped with — and the two decisions must agree per row rather than be right by accident,
    /// which is why the row's expected value is asserted as well.
    /// </summary>
    public static IEnumerable<object[]> IdentityKindIrrelevantCases()
    {
        yield return [Roles.DefaultProviderAdminRoleName, "acme", true];
        yield return [Roles.DefaultProviderAdminRoleName, "globex", false];
        yield return ["Provider", "acme", true];
        yield return ["Provider", "globex", false];
    }

    [Theory]
    [MemberData(nameof(IdentityKindIrrelevantCases))]
    public void Should_DecideIdentically_ForInteractiveAndServiceKeyIdentities(string role, string target, bool expected)
    {
        var interactive = MakeCaller(role, isServiceKeyIdentity: false, "acme");
        var serviceKey = MakeCaller(role, isServiceKeyIdentity: true, "acme");

        var interactiveDecision = ProviderWriteAuthorization.Authorize(interactive, DomainProviderName.From(target), Roles);
        var serviceKeyDecision = ProviderWriteAuthorization.Authorize(serviceKey, DomainProviderName.From(target), Roles);

        Assert.Equal(interactiveDecision, serviceKeyDecision);
        Assert.Equal(expected, interactiveDecision);
    }
}
