using UserOrg.Shared.Hashing;

using Waldur.Contracts.Events;
using Waldur.Sync.Diff;

using Xunit;

namespace Waldur.Sync.Tests.Diff;

public sealed class DiffEngineMembershipTests
{
    private static DiffInput ProjectWithMembership(MembershipSnapshot? membership, bool isRemoved = false)
    {
        var project = DiffTestData.Project(isRemoved: isRemoved);
        return new DiffInput
        {
            Projects = [new ProjectDiffInput(project.ExternalId, project, membership)]
        };
    }

    private static ShaBreadcrumbMap MapWithProjectAnd(params (ShaKey Key, IIdentityHashed Payload)[] members)
    {
        var project = DiffTestData.Project();
        var entries = new List<(ShaKey, IIdentityHashed)> { (ShaKey.Project(project.ExternalId), project) };
        entries.AddRange(members);
        return DiffTestData.MapWith([.. entries]);
    }

    [Fact]
    public void Should_EmitUserAssigned_When_MemberNew()
    {
        var member = DiffTestData.Member("alice");
        var map = MapWithProjectAnd();
        var input = ProjectWithMembership(MembershipSnapshot.Complete([member]));

        var result = DiffTestData.Engine().Diff(input, map);

        var action = Assert.Single(result.Actions);
        var assigned = Assert.IsType<UserAssigned>(action.Event);
        Assert.Same(member, assigned.Payload);
        Assert.Equal(ShaKey.Member(DiffTestData.ProjectId, "alice"), action.OnAck.Key);
        Assert.Equal(ShaMutationKind.Set, action.OnAck.Kind);
    }

    [Fact]
    public void Should_EmitUserRoleChanged_When_RoleHashDiffers()
    {
        var oldMember = DiffTestData.Member("alice", role: "Member");
        var newMember = DiffTestData.Member("alice", role: "Manager");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), oldMember));
        var input = ProjectWithMembership(MembershipSnapshot.Complete([newMember]));

        var result = DiffTestData.Engine().Diff(input, map);

        var action = Assert.Single(result.Actions);
        var changed = Assert.IsType<UserRoleChanged>(action.Event);
        Assert.Same(newMember, changed.Payload);
        Assert.Equal(newMember.ComputeIdentityHash(), action.OnAck.Hash);
    }

    [Fact]
    public void Should_EmitNothing_When_MemberHashEqual()
    {
        var member = DiffTestData.Member("alice");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), member));
        var input = ProjectWithMembership(MembershipSnapshot.Complete([member]));

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_EmitUserRemoved_When_MemberMissingFromCompleteSnapshot()
    {
        var member = DiffTestData.Member("alice");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), member));
        var input = ProjectWithMembership(MembershipSnapshot.Complete([]));

        var result = DiffTestData.Engine().Diff(input, map);

        var action = Assert.Single(result.Actions);
        var removed = Assert.IsType<UserRemoved>(action.Event);
        Assert.Equal(DiffTestData.ProjectId, removed.ProjectExternalId);
        Assert.Equal("alice", removed.UserName);
        Assert.Equal(DiffTestData.Instance, removed.Instance);
        Assert.Equal(ShaMutationKind.Remove, action.OnAck.Kind);
        Assert.Equal(ShaKey.Member(DiffTestData.ProjectId, "alice"), action.OnAck.Key);
    }

    [Fact]
    public void Should_EmitNoMembershipEvents_And_LeaveShas_When_SnapshotFailed()
    {
        var member = DiffTestData.Member("alice");
        var memberKey = ShaKey.Member(DiffTestData.ProjectId, "alice");
        var map = MapWithProjectAnd((memberKey, member));
        var beforeHash = map.GetHashOrNull(memberKey);
        var input = ProjectWithMembership(MembershipSnapshot.Failed());

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Actions);
        Assert.Equal(beforeHash, map.GetHashOrNull(memberKey));
    }

    [Fact]
    public void Should_EmitNoMembershipEvents_When_SnapshotAbsent()
    {
        var member = DiffTestData.Member("alice");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), member));
        var input = ProjectWithMembership(membership: null);

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Should_EmitNoMembershipEvents_When_ProjectRemoved()
    {
        var member = DiffTestData.Member("bob");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), DiffTestData.Member("alice")));
        // Removed project, but a complete snapshot is supplied with a brand-new member: still no membership events.
        var input = ProjectWithMembership(MembershipSnapshot.Complete([member]), isRemoved: true);

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.DoesNotContain(result.Actions, a => a.Event is UserAssigned or UserRoleChanged or UserRemoved);
    }

    [Fact]
    public void Should_EmitExactlyOneAction_When_SnapshotHasDuplicateUserName()
    {
        // Defensive dedup: the engine must not rely on a B03 uniqueness guarantee. First-wins.
        var first = DiffTestData.Member("alice", role: "Member");
        var duplicate = DiffTestData.Member("alice", role: "Manager");
        var map = MapWithProjectAnd();
        var input = ProjectWithMembership(MembershipSnapshot.Complete([first, duplicate]));

        var result = DiffTestData.Engine().Diff(input, map);

        var action = Assert.Single(result.Actions);
        var assigned = Assert.IsType<UserAssigned>(action.Event);
        Assert.Same(first, assigned.Payload);
    }

    [Fact]
    public void Should_NotEmitRemoval_When_DuplicateUserNameIsPresent()
    {
        // Dedup on the emit side must not disturb the removal sweep: alice stays present.
        var alice = DiffTestData.Member("alice");
        var map = MapWithProjectAnd((ShaKey.Member(DiffTestData.ProjectId, "alice"), alice));
        var input = ProjectWithMembership(
            MembershipSnapshot.Complete([alice, DiffTestData.Member("alice", role: "Manager")]));

        var result = DiffTestData.Engine().Diff(input, map);

        Assert.DoesNotContain(result.Actions, a => a.Event is UserRemoved);
    }
}
