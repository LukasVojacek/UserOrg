namespace Core.Shared.Auth;

/// <summary>
/// How much data a read endpoint may return, resolved from the caller by precedence
/// (see CONTEXT.md "Read scope tier"): <see cref="All"/> (admin) &gt; <see cref="Provider"/>
/// (a provider service-key identity, or an interactive ProviderAdmin holding the same claim, ADR-0094)
/// &gt; <see cref="Scoped"/> (the caller's project scope).
/// A closed union — endpoints translate it into their own filter and the <c>switch</c> stays exhaustive.
/// </summary>
internal abstract record ReadScope
{
    private ReadScope() { }

    /// <summary>Admin: every row, no filter.</summary>
    internal sealed record All : ReadScope;

    /// <summary>Provider identity: keyed on the <c>providerName</c> claim — the same key writes use —
    /// never the <c>ProviderAdmin</c> role name. Held by a provider service key or, since ADR-0094, by
    /// an interactive ProviderAdmin. This is the tier, not the final filter: each <b>general</b> list
    /// endpoint unions the named provider with the caller's own project scope (plus the public providers
    /// on <c>GET /providers</c>), while the dedicated <c>provider-admin/*</c> routes pin the tier to the
    /// own provider only, with no union (ADR-0058/0094), except <c>ProviderAdmin/Providers/{providerName}/Resources</c>,
    /// which keeps the project union. A by-natural-key read goes through
    /// <see cref="CallerReadScope"/>'s <c>ResolveProjectScopeFor</c>, which keeps an interactive holder
    /// inside its own provider and falls back to its project scope outside it.</summary>
    internal sealed record Provider(ProviderName ProviderName) : ReadScope;

    /// <summary>Caller: only the projects in the caller's scope (may be empty → sees nothing).</summary>
    internal sealed record Scoped(IReadOnlySet<ProjectShortName> Projects) : ReadScope;
}

/// <summary>
/// Resolves a caller's <see cref="ReadScope"/> by the same precedence
/// <see cref="ProviderWriteAuthorization"/> uses for writes: admin first, then provider identity, then
/// caller scope. The admin check needs <see cref="WellKnownRoles"/>, so this can't be a pure property.
/// </summary>
internal static class CallerReadScope
{
    extension(Caller caller)
    {
        internal bool IsAdmin(WellKnownRoles roles) =>
            string.Equals(caller.Role, roles.DefaultAdminRoleName, StringComparison.OrdinalIgnoreCase);

        internal ReadScope ResolveReadScope(WellKnownRoles roles) =>
            caller.IsAdmin(roles) ? new ReadScope.All()
            : caller.ProviderName is { } providerName ? new ReadScope.Provider(providerName)
            : new ReadScope.Scoped(caller.Scope);

        /// <summary>
        /// The project scope a by-natural-key read must apply to a record owned by <paramref name="targetProvider"/>,
        /// or <c>null</c> for an unrestricted lookup.
        /// </summary>
        internal IReadOnlyList<ProjectShortName>? ResolveProjectScopeFor(ProviderName targetProvider, WellKnownRoles roles) =>
            caller.ResolveReadScope(roles) switch
            {
                // A service-key provider identity resolves any active record by name (ADR-0018); an interactive
                // one has that reach only inside its own provider.
                ReadScope.Provider provider when caller.IsServiceKeyIdentity || provider.ProviderName == targetProvider => null,
                ReadScope.All => null,
                // Every other caller — including an interactive provider identity reading outside its own
                // provider — falls back to its project scope, so holding the provider claim never opens
                // another provider's private records.
                _ => [.. caller.Scope],
            };
    }
}