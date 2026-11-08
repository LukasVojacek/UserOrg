namespace UserOrg.Shared;

/// <summary>
/// Raw caller identity read directly from claims at the endpoint boundary (see ADR-0080 §1).
/// </summary>
/// <remarks>
/// Deliberately does not carry an internal <c>Guid UserId</c>: resolving the identity-provider user id to
/// the internal user requires a DB/service lookup, and this type must stay reachable from a plain
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> extension with no DbContext dependency. Callers that
/// need the internal id resolve <see cref="IdentityProviderUserId"/> themselves the same way the module
/// already does elsewhere (e.g. via a query handler).
/// </remarks>
/// <param name="IdentityProviderUserId">The subject claim value assigned by the identity provider, or <see langword="null"/> when not a JWT bearer request.</param>
/// <param name="ProviderId">The caller's provider id from the <c>provider_id</c> claim, or <see langword="null"/> when absent.</param>
/// <param name="IsPlatformAdmin">Whether the caller carries the platform-administrator role claim.</param>
public sealed record CallerInfo(string? IdentityProviderUserId, Guid? ProviderId, bool IsPlatformAdmin);