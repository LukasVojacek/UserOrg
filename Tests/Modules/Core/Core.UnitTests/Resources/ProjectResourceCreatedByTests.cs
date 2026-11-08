using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Resource;
using Core.Features.Resource.GetResource;
using Core.Features.Resource.GetResourcesProviderAdmin;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Resources;

/// <summary>
/// Tests for ProjectResource.CreatedBy tracking behavior:
/// - CreatedByEmail is returned in GET endpoints (detail, admin list)
/// - CreatedByEmail is null when creator User record does not exist
/// - Endpoint tests verify that the query projection and response mapping work correctly
/// </summary>
public sealed class ProjectResourceCreatedByTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");
    private const string ProviderNameStr = "acme";
    private const string ResourceNameStr = "test-resource";
    private const string CreatorEmail = "creator@example.org";
    private const string CreatorEmail2 = "second@example.org";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);
    private static readonly WellKnownRoles Roles = new();

    private static Caller AdminCaller() => new(
        new IdpId("admin"), Roles.DefaultAdminRoleName, null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static IOptions<WellKnownRoles> RoleOptions() => Options.Create(Roles);
    private static IOptions<PaginationOptions> Pagination() => Options.Create(new PaginationOptions());

    private static ResourceSummary ResourceWithCreator(string name, string creatorEmail) => new(
        name,
        $"{ProviderNameStr}|{name}",
        "Test resource",
        "pi@example.org",
        Start,
        End,
        Core.Domain.Shared.EOperationMode.Editable,
        "https://example.org/license",
        EProjectResourceState.Published,
        Email.Create(creatorEmail),
        []);

    private static ResourceSummary ResourceWithoutCreator(string name) => new(
        name,
        $"{ProviderNameStr}|{name}",
        "Resource with departed creator",
        "pi@example.org",
        Start,
        End,
        Core.Domain.Shared.EOperationMode.Editable,
        null,
        EProjectResourceState.Published,
        null,  // Creator user record does not exist
        []);

    // Test 3: GET ProviderAdmin list includes CreatedByEmail for all resources

    [Fact]
    public async Task Should_IncludeCreatedByEmail_InProviderAdminList()
    {
        var caller = new Caller(
            new IdpId("provider-admin"), "ProviderAdmin", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            ProviderName.Create(ProviderNameStr), IsServiceKeyIdentity: true);

        var resource1 = ResourceWithCreator("resource-1", CreatorEmail);
        var resource2 = ResourceWithCreator("resource-2", CreatorEmail2);

        var resourceQuery = new Mock<IResourceQuery>();
        resourceQuery.Setup(q => q.GetResourcesAsync(
            It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ResourceSummary>([resource1, resource2], 2));

        var result = await GetResourcesProviderAdminEndpoint.HandleAsync(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), ProviderNameStr, null, null, null, null, caller,
            new PageRequest(null, null), resourceQuery.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var okResult = Assert.IsType<Ok<IReadOnlyList<ResourceAdminResponse>>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);
        Assert.Equal(2, response.Count);

        var res1 = response.First(r => r.Name == "resource-1");
        var res2 = response.First(r => r.Name == "resource-2");

        Assert.Equal(CreatorEmail, res1.CreatedByEmail);
        Assert.Equal(CreatorEmail2, res2.CreatedByEmail);
    }

    // Test 4: GET ProviderAdmin list handles null CreatedByEmail correctly

    [Fact]
    public async Task Should_HandleNullCreatedByEmail_InProviderAdminList()
    {
        var caller = new Caller(
            new IdpId("provider-admin"), "ProviderAdmin", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
            ProviderName.Create(ProviderNameStr), IsServiceKeyIdentity: true);

        var resource1 = ResourceWithCreator("resource-1", CreatorEmail);
        var resource2 = ResourceWithoutCreator("resource-departed-creator");

        var resourceQuery = new Mock<IResourceQuery>();
        resourceQuery.Setup(q => q.GetResourcesAsync(
            It.IsAny<ResourceFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ResourceSummary>([resource1, resource2], 2));

        var result = await GetResourcesProviderAdminEndpoint.HandleAsync(
            new Microsoft.AspNetCore.Http.DefaultHttpContext(), ProviderNameStr, null, null, null, null, caller,
            new PageRequest(null, null), resourceQuery.Object, RoleOptions(), Pagination(), CancellationToken.None);

        var okResult = Assert.IsType<Ok<IReadOnlyList<ResourceAdminResponse>>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);
        Assert.Equal(2, response.Count);

        var res1 = response.First(r => r.Name == "resource-1");
        var res2 = response.First(r => r.Name == "resource-departed-creator");

        Assert.Equal(CreatorEmail, res1.CreatedByEmail);
        Assert.Null(res2.CreatedByEmail);
    }

    // Test 5: Query projection correctly maps CreatedBy to CreatedByEmail

    [Fact]
    public async Task Should_ProjectCreatedByToCreatedByEmail_InQueryResult()
    {
        var caller = AdminCaller();
        var creatorEmail = Email.Create(CreatorEmail);

        var resourceWithEmail = new ResourceSummary(
            ResourceNameStr,
            $"{ProviderNameStr}|{ResourceNameStr}",
            "Test",
            "pi@example.org",
            Start,
            End,
            Core.Domain.Shared.EOperationMode.Editable,
            null,
            EProjectResourceState.Draft,
            creatorEmail,
            []);

        var resourceQuery = new Mock<IResourceQuery>();
        resourceQuery.Setup(q => q.GetResourceByProviderAndNameAsync(
            ProviderName.Create(ProviderNameStr), ResourceName.Create(ResourceNameStr),
            It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resourceWithEmail);

        var result = await GetResourceEndpoint.HandleAsync(
            new GetResourceEndpoint.Request(ProviderNameStr, ResourceNameStr),
            caller, resourceQuery.Object, RoleOptions(), CancellationToken.None);

        var okResult = Assert.IsType<Ok<ResourceResponse>>(result);
        var response = okResult.Value;
        Assert.NotNull(response);

        // Verify query was called with expected parameters
        resourceQuery.Verify(
            q => q.GetResourceByProviderAndNameAsync(
                ProviderName.Create(ProviderNameStr), ResourceName.Create(ResourceNameStr),
                It.IsAny<IReadOnlyList<ProjectShortName>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
