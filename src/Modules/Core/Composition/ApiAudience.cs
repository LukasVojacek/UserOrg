namespace Core.Composition;

/// <summary>
/// Audience names for the role-scoped v2 OpenAPI documents. Used as the ApiExplorer
/// <c>GroupName</c> on a v2 endpoint to place it in the matching document
/// (<c>v2-User</c> / <c>v2-ProviderAdmin</c> / <c>v2-Admin</c>).
///
/// This is a documentation-grouping concern only — it decides which document an endpoint
/// is *described* in, not who may call it. Authorization is enforced at call time by the
/// Gateway (RBAC), independently of these tags. A v2 endpoint with no audience tag defaults
/// to <see cref="User"/>.
/// </summary>
public static class ApiAudience
{
    public const string User = "User";
    public const string ProviderAdmin = "ProviderAdmin";
    public const string Admin = "Admin";
}