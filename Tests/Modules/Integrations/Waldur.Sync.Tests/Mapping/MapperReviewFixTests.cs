using UserOrg.Shared;
using UserOrg.Shared.Persistance;

using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

/// <summary>
/// Guards for the independent-review findings: W1 (order-dependent hashed scalars) and M1
/// (constants drift vs. UserOrg.Shared).
/// </summary>
public sealed class MapperReviewFixTests
{
    // ── W1: reversed-input hash equality ─────────────────────────────────────

    [Fact]
    public void Should_ProduceSameProviderHash_When_OfferingInputOrderReversed()
    {
        var mapper = MapperTestData.Mapper();
        // Same SSH URL → one derived location; the two offerings differ in name casing (LCP fold
        // input), Type, IsShared and Description — all previously plucked by input order.
        var a = MapperTestData.Offering(
            id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            name: "karolina-CPU", type: "HPC", shared: true,
            endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina.it4i.cz")]);
        var b = MapperTestData.Offering(
            id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
            name: "karolina-GPU", type: "HPC", shared: false,
            endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina.it4i.cz")]);

        var forward = mapper.MapProvider(MapperTestData.Customer(), [a, b]);
        var reversed = mapper.MapProvider(MapperTestData.Customer(), [b, a]);

        Assert.Equal(
            forward.Payload!.ComputeIdentityHash(),
            reversed.Payload!.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ProduceSameProjectHash_When_ResourceInputOrderReversed()
    {
        var mapper = MapperTestData.Mapper();
        // Same group (same effective/backend/customer); the two members differ in Description (Name)
        // and Created — the Description scalar was previously plucked from members[0] by input order.
        var r1 = new ResourceOffering(
            MapperTestData.Resource(
                id: Guid.Parse("cccccccc-0000-0000-0000-000000000001"),
                name: "res-name-1",
                created: new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            MapperTestData.Offering());
        var r2 = new ResourceOffering(
            MapperTestData.Resource(
                id: Guid.Parse("dddddddd-0000-0000-0000-000000000002"),
                name: "res-name-2",
                created: new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            MapperTestData.Offering());

        var forward = mapper.MapProject(MapperTestData.Project(), [r1, r2]);
        var reversed = mapper.MapProject(MapperTestData.Project(), [r2, r1]);

        Assert.Equal(
            forward.Payload!.ComputeIdentityHash(),
            reversed.Payload!.ComputeIdentityHash());
    }

    [Fact]
    public void Should_DeriveStableLocationName_RegardlessOfCasingOrder()
    {
        var mapper = MapperTestData.Mapper();
        var a = MapperTestData.Offering(id: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            name: "Karolina-CPU", endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina")]);
        var b = MapperTestData.Offering(id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
            name: "karolina-GPU", endpoints: [MapperTestData.SshEndpoint("ssh://login.karolina")]);

        var forward = mapper.MapProvider(MapperTestData.Customer(), [a, b]).Payload!.Locations[0].Name;
        var reversed = mapper.MapProvider(MapperTestData.Customer(), [b, a]).Payload!.Locations[0].Name;

        Assert.Equal(forward, reversed);
    }

    // ── M1: constants drift guard against UserOrg.Shared ─────────────────────

    // The internal MappingConstants is visible here via InternalsVisibleTo(Waldur.Sync.Tests).

    [Fact]
    public void Should_MatchDbConstants_LengthLimits()
    {
        Assert.Equal(DbConstants.NameMaxLength, MappingConstants.NameMaxLength);
        Assert.Equal(DbConstants.DescriptionMaxLength, MappingConstants.DescriptionMaxLength);
        Assert.Equal(DbConstants.ExtendedDescriptionMaxLength, MappingConstants.ExtendedDescriptionMaxLength);
    }

    [Fact]
    public void Should_MatchRoleInfoConstants_MappedRoleNames()
    {
        Assert.Equal(RoleInfoConstants.ProjectOwnerRoleName, MappingConstants.UserorgOwnerRoleName);
        Assert.Equal(RoleInfoConstants.ProjectManagerRoleName, MappingConstants.UserorgManagerRoleName);
        Assert.Equal(RoleInfoConstants.ProjectMemberRoleName, MappingConstants.UserorgMemberRoleName);
    }

    [Fact]
    public void Should_MatchApplicationConstants_HeappeLocalBasePathKey()
    {
        Assert.Equal(ApplicationConstants.HEAppELocalBasePathKey, MappingConstants.HeappeLocalBasePathKey);
    }
}
