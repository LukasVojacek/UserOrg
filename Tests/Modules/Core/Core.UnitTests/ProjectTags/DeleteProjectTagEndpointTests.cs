using Core.DataAccess.Queries.Contracts;
using Core.Features.ProjectTag.DeleteProjectTag;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProjectTagAggregate = Core.Domain.Project.ProjectTag;

namespace Core.UnitTests.ProjectTags;

/// <summary>
/// Contract tests for the admin-only catalogue delete endpoint (F1 Phase C): 204 on a clean delete,
/// 404 when missing/already-deleted, 409 when still assigned (block-if-in-use, G2), 403 for non-admins,
/// 400 for an invalid name.
/// </summary>
public sealed class DeleteProjectTagEndpointTests
{
    private static Caller AdminCaller() => new(
        new IdpId("admin"), "Admin", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller NonAdminCaller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static Caller MixedCaseAdminCaller() => new(
        new IdpId("admin"), "aDmIn", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static void AssertRoleDiffersFromConfiguredAdminOnlyInCase(Caller caller)
    {
        var configured = Roles().Value.DefaultAdminRoleName;
        Assert.NotEqual(configured, caller.Role);
        Assert.Equal(configured, caller.Role, ignoreCase: true);
    }

    private static IOptions<WellKnownRoles> Roles() => Options.Create(new WellKnownRoles());

    private static int? StatusOf(IResult result) =>
        Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static Task<IResult> InvokeAsync(
        Caller caller,
        string tagName = "hpc",
        ProjectTagAggregate? tag = null,
        bool inUse = false,
        Mock<IUnitOfWork>? unitOfWork = null)
    {
        var repository = new Mock<IRepository<ProjectTagAggregate, TagName>>();
        repository.Setup(r => r.FindAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(tag));

        var query = new Mock<IProjectTagQuery>();
        query.Setup(q => q.IsTagAssignedToAnyProjectAsync(It.IsAny<TagName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inUse);

        unitOfWork ??= new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return DeleteProjectTagEndpoint.HandleAsync(
            tagName, caller, repository.Object, query.Object, Roles(), unitOfWork.Object,
            new RecordingAuditLogger<DeleteProjectTagEndpoint.AuditCategory>(), NullLogger<DeleteProjectTagEndpoint.AuditCategory>.Instance, CancellationToken.None);
    }

    private static ProjectTagAggregate Tag() =>
        ProjectTagAggregate.Create(TagName.Create("hpc"), UserName.Create("author"));

    [Fact]
    public async Task Should_ReturnNoContent_When_DeletesUnusedTag()
    {
        var tag = Tag();
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await InvokeAsync(AdminCaller(), tag: tag, inUse: false, unitOfWork: unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.True(tag.IsDeleted);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_TagMissing()
    {
        var result = await InvokeAsync(AdminCaller(), tag: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_TagInUse()
    {
        var tag = Tag();

        var result = await InvokeAsync(AdminCaller(), tag: tag, inUse: true);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(result));
        Assert.False(tag.IsDeleted);
    }

    [Fact]
    public async Task Should_ReturnNoContent_When_CallerRoleMatchesAdminRoleOnlyIgnoringCase()
    {
        var caller = MixedCaseAdminCaller();
        AssertRoleDiffersFromConfiguredAdminOnlyInCase(caller);
        var tag = Tag();

        var result = await InvokeAsync(caller, tag: tag);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        Assert.True(tag.IsDeleted);
    }

    [Fact]
    public async Task Should_ReturnForbidden_When_NotAdmin()
    {
        var result = await InvokeAsync(NonAdminCaller(), tag: Tag());

        Assert.Equal(StatusCodes.Status403Forbidden, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnValidationProblem_When_NameInvalid()
    {
        var result = await InvokeAsync(AdminCaller(), tagName: "   ");

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(ProjectErrorCodes.TagNameInvalid, problem.ProblemDetails.Extensions["code"]);
    }
}
