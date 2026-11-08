using DomainProjectTag = Core.Domain.Project.ProjectTag;

namespace Core.UnitTests.ProjectTags;

public sealed class ProjectTagDomainTests
{
    private static DomainProjectTag NewTag() =>
        DomainProjectTag.Create(TagName.Create("hpc"), UserName.Create("author"));

    [Fact]
    public void Should_SetIsDeleted_When_Delete()
    {
        var tag = NewTag();

        var result = tag.Delete();

        Assert.True(result.IsSuccess);
        Assert.True(tag.IsDeleted);
        Assert.NotNull(tag.ModifiedAt);
    }

    [Fact]
    public void Should_Fail_When_DeleteAlreadyDeleted()
    {
        var tag = NewTag();
        Assert.True(tag.Delete().IsSuccess);

        var result = tag.Delete();

        Assert.False(result.IsSuccess);
        Assert.Equal(ProjectTagErrorCodes.AlreadyDeleted, result.Error.Code);
    }

    [Fact]
    public void Should_ClearSoftDeleteFlag_When_Restore()
    {
        var tag = NewTag();
        Assert.True(tag.Delete().IsSuccess);

        tag.Restore();

        Assert.False(tag.IsDeleted);
        Assert.NotNull(tag.ModifiedAt);
    }

    [Fact]
    public void Should_BeIdempotent_When_RestoreCalledOnActiveTag()
    {
        var tag = NewTag();
        Assert.False(tag.IsDeleted);

        tag.Restore();

        Assert.False(tag.IsDeleted);
    }

    [Fact]
    public void Should_CarryStableCode_OnEachProjectTagError()
    {
        var name = TagName.Create("hpc");

        Assert.Equal(ProjectTagErrorCodes.TagNotFound, ProjectTagErrors.TagNotFound(name).Code);
        Assert.Equal(ProjectTagErrorCodes.TagInUse, ProjectTagErrors.TagInUse(name).Code);
        Assert.Equal(ProjectTagErrorCodes.AlreadyDeleted, ProjectTagErrors.AlreadyDeleted(name).Code);
    }
}
