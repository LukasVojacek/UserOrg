namespace UserOrg.Gateway.Configuration;

/// <summary>
/// Develop-only authentication override (ADR-0045). When enabled in a Development environment, the
/// Gateway skips the IdP and treats any non-empty bearer string as the caller's idpid (<c>sub</c>),
/// impersonating that user, so local / DB-backup work does not depend on the IdP.
/// </summary>
/// <remarks>
/// This binds configuration only; the safety gating (must be Development, must be enabled) lives in
/// the auth pipeline. See <c>ServicesExtensions.AddAuthenticationServices</c>.
/// </remarks>
internal sealed class DevAuthOverrideOptions
{
    public const string SectionName = "DevAuthOverride";

    /// <summary>
    /// Gets a value indicating whether the override is switched on. Off by default (opt-in): while off,
    /// real bearer tokens flow through the normal IdP path. Only ever registered in Development.
    /// </summary>
    public bool Enabled { get; init; }
}