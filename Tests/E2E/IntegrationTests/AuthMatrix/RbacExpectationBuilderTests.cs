namespace IntegrationTests.AuthMatrix;

/// <summary>
/// RbacExpectationBuilder is pure: fed the RBAC export text (the same <c>roleManagmentRBAC.json</c>
/// UserOrg.Gateway.csproj ships as its seed at runtime, read here directly off disk rather than
/// through a running Gateway), no HTTP/topology dependency.
/// </summary>
public sealed class RbacExpectationBuilderTests
{
    private static readonly RbacDocument Rbac = RbacDocument.Parse(ReadRbacSeed());

    private static readonly Route UnmappedRoute = new("/api/v2/__no-rbac-mapping__", "POST");

    [Theory]
    [InlineData("Admin", ExpectedOutcome.Allowed)]
    [InlineData("Owner", ExpectedOutcome.Forbidden403)]
    [InlineData("Manager", ExpectedOutcome.Forbidden403)]
    [InlineData("Member", ExpectedOutcome.Forbidden403)]
    [InlineData("Anonymous", ExpectedOutcome.Unauthorized401)]
    public void Should_RestrictToAdmin_When_RouteRequiresPlatformAdminPermission(string role, ExpectedOutcome expected)
    {
        // "v2 List all Users in platform": permissionKeys = ["perm:platform:admin"], held only by Admin.
        var route = new Route("/api/v2/Users", "GET");

        Assert.Equal(expected, RbacExpectationBuilder.Expect(route, role, Rbac));
    }

    [Theory]
    [InlineData("Admin", ExpectedOutcome.Allowed)]
    [InlineData("Owner", ExpectedOutcome.Allowed)]
    [InlineData("Manager", ExpectedOutcome.Allowed)]
    [InlineData("Member", ExpectedOutcome.Forbidden403)]
    [InlineData("Anonymous", ExpectedOutcome.Unauthorized401)]
    public void Should_AllowOwnerAndManager_When_RouteRequiresRequestManagementPermission(string role, ExpectedOutcome expected)
    {
        // "v2 Accept project onboarding request": permissionKeys = ["perm:user:request:manage"],
        // held by Admin/Owner/Manager but not Member.
        var route = new Route("/api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Accept", "PUT");

        Assert.Equal(expected, RbacExpectationBuilder.Expect(route, role, Rbac));
    }

    [Theory]
    [InlineData("Admin", ExpectedOutcome.Allowed)]
    [InlineData("Owner", ExpectedOutcome.Allowed)]
    [InlineData("Manager", ExpectedOutcome.Allowed)]
    [InlineData("Member", ExpectedOutcome.Allowed)]
    [InlineData("Anonymous", ExpectedOutcome.Unauthorized401)]
    public void Should_AllowAnyAuthenticatedRole_When_RouteHasNoPermissionRequirement(string role, ExpectedOutcome expected)
    {
        // "v2 UserInfo": permissionKeys = [] (any-authenticated), but not on the public allowlist.
        var route = new Route("/api/v2/UserInfo", "GET");

        Assert.Equal(expected, RbacExpectationBuilder.Expect(route, role, Rbac));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("Manager")]
    [InlineData("Member")]
    [InlineData("Anonymous")]
    public void Should_AllowEveryTier_When_RouteIsOnThePublicAllowlist(string role)
    {
        // "v2 Request information": permissionKeys = [], under the "/api/v2/Public" prefix
        // PublicEndpointsOptions.GetDefaultPaths() allowlists.
        var route = new Route("/api/v2/Public/Requests/ByLink/{actionLink}", "GET");

        Assert.Equal(ExpectedOutcome.Allowed, RbacExpectationBuilder.Expect(route, role, Rbac));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("Manager")]
    [InlineData("Member")]
    [InlineData("Anonymous")]
    public void Should_AllowEveryTier_When_RouteIsTheExactPublicV2VersionPath(string role)
    {
        var route = new Route("/api/v2/Version", "GET");

        Assert.Equal(ExpectedOutcome.Allowed, RbacExpectationBuilder.Expect(route, role, Rbac));
    }

    [Fact]
    public void Should_ExpectUnauthorizedForAnonymous_When_RouteIsBelowThePublicV2VersionPath()
    {
        var route = new Route("/api/v2/Version/details", "GET");

        Assert.Equal(ExpectedOutcome.Unauthorized401, RbacExpectationBuilder.Expect(route, RbacExpectationBuilder.AnonymousRole, Rbac));
    }

    [Fact]
    public void Should_MatchByPositionRatherThanParameterName_When_RbacTemplateUsesADifferentPlaceholderName()
    {
        // "v2 Get Project Tag" is mapped in roleManagmentRBAC.json as "/api/v2/ProjectTags/{id}",
        // while the live route (and its OpenAPI-discovered form) is "/api/v2/ProjectTags/{tagName}" —
        // same route, different placeholder name. The join must be name-agnostic on path parameters.
        var route = new Route("/api/v2/ProjectTags/{tagName}", "GET");

        Assert.Equal(ExpectedOutcome.Allowed, RbacExpectationBuilder.Expect(route, "Admin", Rbac));
    }

    // The 4 service-token routes (DDI/Service module) require perm:service:*/perm:servicetype:ddi
    // keys that no human role (Admin/Owner/Manager/Member) holds in roleManagmentRBAC.json — they
    // resolve to Forbidden403 mechanically through the same permission-overlap join used above,
    // with no special case in RbacExpectationBuilder.
    public static readonly TheoryData<Route> ServiceTokenRoutes = new()
    {
        new Route("/api/v2/DataStaging/WorkerData", "GET"),
        new Route("/api/v2/DataStaging/DataStagingLocations", "GET"),
        new Route("/api/v2/Service/SyncDetails", "GET"),
        new Route("/api/v2/Service/ProjectsInfo", "GET")
    };

    [Theory]
    [MemberData(nameof(ServiceTokenRoutes))]
    public void Should_ExpectForbiddenForAllHumanRoles_When_RouteIsServiceToken(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);

        foreach (var humanRole in new[] { "Admin", "Owner", "Manager", "Member" })
        {
            Assert.Equal(ExpectedOutcome.Forbidden403, RbacExpectationBuilder.Expect(route, humanRole, Rbac));
        }

        Assert.Equal(ExpectedOutcome.Unauthorized401, RbacExpectationBuilder.Expect(route, "Anonymous", Rbac));
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("Manager")]
    [InlineData("Member")]
    public void Should_FailClosed_When_RouteHasNoRbacMapping(string role)
    {
        // The gateway fails closed on a route with no endpointMappings entry, regardless of the
        // caller's permissions (repo memory: "unmapped route = FAIL-CLOSED 403") — this is not
        // special-cased, an absent RBAC entry mechanically yields Forbidden403. The route is
        // synthetic on purpose: a real route used here goes stale the moment it gets mapped.
        Assert.Equal(ExpectedOutcome.Forbidden403, RbacExpectationBuilder.Expect(UnmappedRoute, role, Rbac));
    }

    [Fact]
    public void Should_RequireAuthentication_When_AnonymousHitsAnUnmappedRoute()
    {
        Assert.Equal(ExpectedOutcome.Unauthorized401, RbacExpectationBuilder.Expect(UnmappedRoute, "Anonymous", Rbac));
    }

    private static string ReadRbacSeed()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "roleManagmentRBAC.json");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root (roleManagmentRBAC.json) could not be located.");
    }
}
