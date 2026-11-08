using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

public sealed class MembershipMappingTests
{
    [Theory]
    [InlineData("PROJECT.MANAGER", "Manager")]
    [InlineData("PROJECT.MEMBER", "Member")]
    [InlineData("PROJECT.ADMIN", "Owner")]
    public void Should_MapWaldurRole_ToUserorgRole(string waldurRole, string expected)
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User(roleName: waldurRole)]);

        Assert.Equal(expected, Assert.Single(result.Members).Role);
        Assert.DoesNotContain(result.Skips, s => s.Reason == EMappingSkipReason.MemberUnknownRole);
    }

    [Fact]
    public void Should_FallBackToMember_And_Warn_When_RoleUnknown()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User(userName: "bob", roleName: "PROJECT.WEIRD")]);

        // Emitted with the Member fallback (consistent with the consumer) so hashes stay stable.
        Assert.Equal("Member", Assert.Single(result.Members).Role);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.MemberUnknownRole);
    }

    [Fact]
    public void Should_SetHasAccessTrue_ForAllMembers()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User()]);

        Assert.True(Assert.Single(result.Members).HasAccess);
    }

    [Fact]
    public void Should_CarryEmail_AsCreateOnly()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User(email: "x@y.z")]);

        Assert.Equal("x@y.z", Assert.Single(result.Members).Email);
    }

    [Fact]
    public void Should_SkipMember_When_UserNameMissing()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User(userName: "  ")]);

        Assert.Empty(result.Members);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.MemberUserNameMissing);
    }

    [Fact]
    public void Should_UseUserNameAsIdentity_And_CanonicalProjectExternalId()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapMembership(MapperTestData.ProjectId, [MapperTestData.User(userName: "alice")]);

        var member = Assert.Single(result.Members);
        Assert.Equal("alice", member.UserName);
        Assert.Equal("22222222-2222-2222-2222-222222222222", member.ProjectExternalId);
    }
}
