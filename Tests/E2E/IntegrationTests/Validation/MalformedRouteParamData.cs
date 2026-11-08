namespace IntegrationTests.Validation;

/// <summary>
/// The malformed-route-param test vectors for <see cref="RouteParamValidationTests"/>. Each case pairs a
/// route (with exactly one segment corrupted, every other segment a well-formed-but-absent placeholder)
/// with the status code the endpoint's own validation legitimately produces for that corruption: a
/// <c>[FromRoute] string</c> parameter the handler parses itself via <c>TryCreate</c>/<c>TryParse</c>
/// before doing anything else, a value object bound directly by the framework (<c>[FromRoute]
/// ProjectShortName</c>, <c>[FromRoute] Guid</c>), or (for the one endpoint with no format rule at all)
/// the ordinary lookup-miss 404.
///
/// The corrupting value is a single URL-encoded space (<c>%20</c>): it is a non-empty path segment, so
/// routing still dispatches to the endpoint (an empty segment would 404 on routing before ever reaching the
/// handler, which is a routing-table fact, not a parameter-validation one), but every value object here
/// rejects whitespace-only input.
///
/// This file covers a deliberately *smaller* set than B04's route-inventory (11 DELETE endpoints + every
/// GET/PUT/POST with a route-bound Guid or natural-key value object) because of the "trivial route param"
/// skips below; every non-trivial endpoint from that inventory now has a case (issue #260 is fixed).
///
/// <b>Remark:</b> issue #260 tracked
/// <see href="https://opencode.it4i.eu/lexis-platform/backend/userorg/-/work_items/260">on GitLab</see> a
/// 500-on-malformed-param defect: 16 endpoints binding a value object directly as a typed route parameter
/// (e.g. <c>[FromRoute] ProjectShortName</c>, <c>[FromRoute] Guid</c>) hit ASP.NET Core's model binder,
/// which throws <c>Microsoft.AspNetCore.Http.BadHttpRequestException</c> on a failed <c>TryParse</c> — and
/// <c>GlobalExceptionHandler</c> had no case for it, so every such exception fell into the generic
/// <c>default</c> branch and returned 500. Two more endpoints (<c>GetProviderEndpoint</c>,
/// <c>PromoteToProviderAdminEndpoint</c>) resolved a route value via the throwing <c>ProviderName.From()</c>
/// instead of the non-throwing <c>TryCreate</c> every sibling endpoint uses, hitting the same <c>default</c>
/// 500 branch via an uncaught <c>ArgumentException</c>. Both root causes are fixed: <c>GlobalExceptionHandler</c>
/// now has an explicit <c>BadHttpRequestException</c> branch returning 400 with <c>code</c>/<c>detail</c>
/// (the 16 endpoints below), and both <c>ProviderName</c> endpoints now use <c>TryCreate</c> and return 400
/// through the v2 <c>Problems.cs</c> helper. See <see cref="RouteParamValidationTests"/> for the ADR-0020
/// body assert and the handler-path/<c>Problems.cs</c>-path shape-parity check.
///
/// <c>PromoteToProviderAdminEndpoint</c> is not a row in this table (see the comment above the
/// <c>GetProvider</c> row): it needs a real seeded <c>{idpUserId}</c>, which this static table has no
/// fixture instance to supply. It is covered by a dedicated <c>[Fact]</c> in
/// <see cref="RouteParamValidationTests"/> instead.
/// </summary>
internal static class MalformedRouteParamData
{
    private const string Whitespace = "%20";

    // Placeholders for segments that are not the one under test. Any well-formed-shaped, non-matching
    // value works: the case fails on the corrupted segment before a lookup for these could ever run.
    private const string Placeholder = "route-param-validation-placeholder";

    /// <summary>
    /// Endpoints with a route-bound parameter that is a plain <c>string</c> with no parse/format rule at
    /// all — the handler forwards it straight into a repository/query lookup, so no input is "malformed",
    /// it just may or may not resolve to something. Skipped per B04's scope (only DELETE-legitimate-4xx and
    /// non-trivial GET/PUT/POST params are covered); listed explicitly rather than silently omitted:
    /// - GetPublicRequestInfoEndpoint / HandleActionLinkEndpoint ({actionLink}) — opaque single-use token.
    /// - GetResourceRequestActionEndpoint / HandleResourceRequestActionEndpoint ({token}) — opaque token.
    /// - AcceptRequestEndpoint / RejectRequestEndpoint ({slug}), AcceptOnboardingRequestEndpoint /
    ///   RejectOnboardingRequestEndpoint ({projectShortName}/{slug} slug part), CancelResourceRequestEndpoint,
    ///   ResendApprovalEmailEndpoint ({slug}) — GUID-free slugs (ADR-0024), free-form lookup key.
    /// - AssignProjectUserEndpoint / ChangeProjectUserRoleEndpoint's {idpUserId} — wrapped via
    ///   <c>new IdpId(value)</c> (the non-validating record constructor, not <c>IdpId.TryParse</c>), so any
    ///   non-empty string is "well-formed" as far as the endpoint is concerned; a value that resolves to no
    ///   user degrades to the ordinary NotFound business path (already covered by B02's auth matrix), not a
    ///   distinct malformed-route-param outcome. Both endpoints still get a case below, corrupting their
    ///   other, non-trivial route segment. PromoteToProviderAdminEndpoint's {idpUserId} is equally trivial,
    ///   but that endpoint looks up the user *before* validating {providerName} (its one non-trivial
    ///   segment), so a placeholder {idpUserId} here would 404 before the provider-name case is ever
    ///   reached — see the comment above <c>GetProvider</c>'s row for where it is covered instead.
    /// - ResetAllCachesEndpoint (DELETE caches) — no route parameters at all.
    /// </summary>
    internal static TheoryData<string, string, string, int> BuildCases()
    {
        var data = new TheoryData<string, string, string, int>();

        // --- DELETE (10 of the 11 endpoints; ResetAllCachesEndpoint has no route params) ---
        data.Add("DeleteProjectTag", "DELETE", $"/api/v2/ProjectTags/{Whitespace}", 400);
        data.Add("DeleteResource", "DELETE", $"/api/v2/Providers/{Whitespace}/Resources/{Placeholder}", 400);
        data.Add("UnassignProjectResource", "DELETE", $"/api/v2/Projects/{Whitespace}/Providers/{Placeholder}/Resources/{Placeholder}", 400);
        data.Add("DeactivateProvider", "DELETE", $"/api/v2/Providers/{Whitespace}", 400);
        data.Add("RemoveLocation", "DELETE", $"/api/v2/Providers/{Whitespace}/Locations/{Placeholder}", 400);
        data.Add("RemoveAssignment", "DELETE", $"/api/v2/Providers/{Whitespace}/Locations/{Placeholder}/Assignments/{Placeholder}", 400);
        // {idpUserId} is trivial (see class doc), but DELETE still needs one case — a whitespace idpid
        // never matches a seeded user, so the lookup legitimately 404s.
        data.Add("ResetProviderAdmin", "DELETE", $"/api/v2/Users/{Whitespace}/ProviderAdmin", 404);
        data.Add("DeleteProject", "DELETE", $"/api/v2/Projects/{Whitespace}", 400);
        data.Add("UnassignProjectTag", "DELETE", $"/api/v2/Projects/{Whitespace}/Tags/{Placeholder}", 400);
        data.Add("UnassignProjectUser", "DELETE", $"/api/v2/Projects/{Whitespace}/Users/{Placeholder}", 400);

        // --- GET with a plain-string route param the handler validates itself ---
        data.Add("GetLocation", "GET", $"/api/v2/Providers/{Whitespace}/Locations/{Placeholder}", 400);
        data.Add("GetResource", "GET", $"/api/v2/Providers/{Whitespace}/Resources/{Placeholder}", 400);
        data.Add("GetProjectTag", "GET", $"/api/v2/ProjectTags/{Whitespace}", 400);

        // --- GET/PUT/POST with a route-bound value object or Guid, bound directly by the framework ---
        data.Add("GetResourceRequestStatus", "GET", $"/api/v2/ResourceRequests/{Whitespace}", 400);
        data.Add("GetProject", "GET", $"/api/v2/Projects/{Whitespace}", 400);
        data.Add("GetExtendedProject", "GET", $"/api/v2/Projects/{Whitespace}/Extended", 400);
        data.Add("GetProjectTemplates", "GET", $"/api/v2/Projects/{Whitespace}/Templates", 400);
        data.Add("GetProjectUsers", "GET", $"/api/v2/Projects/{Whitespace}/Users", 400);
        data.Add("GetValidRoles", "GET", $"/api/v2/Projects/{Whitespace}/ValidRoles", 400);
        data.Add("GetProjectResourceRequests", "GET", $"/api/v2/Projects/{Whitespace}/ResourceRequests", 400);
        data.Add("UpdateProject", "PUT", $"/api/v2/Projects/{Whitespace}", 400);
        data.Add("AssignProjectTag", "PUT", $"/api/v2/Projects/{Whitespace}/Tags/{Placeholder}", 400);
        data.Add("AssignProjectUser", "PUT", $"/api/v2/Projects/{Whitespace}/Users/{Placeholder}", 400);
        data.Add("ChangeProjectUserRole", "PUT", $"/api/v2/Projects/{Whitespace}/Users/{Placeholder}/Role", 400);
        data.Add("CreateProjectTag", "POST", $"/api/v2/Projects/{Whitespace}/Tags", 400);

        // --- GET with a plain-string ProviderName the handler resolves via TryCreate + Problems.cs ---
        data.Add("GetProvider", "GET", $"/api/v2/Providers/{Whitespace}", 400);

        // PromoteToProviderAdminEndpoint (the other TryCreate/Problems.cs-path fix) is NOT a row here: its
        // route validates {providerName} after looking up {idpUserId} (see HandleAsync), and a placeholder
        // idpUserId would 404 before the provider-name case under test is ever reached. This table has no
        // fixture instance to supply a real seeded idpUserId — covered instead by
        // RouteParamValidationTests.Should_ReturnBadRequest_When_PromoteToProviderAdminProviderNameIsMalformed.

        return data;
    }
}
