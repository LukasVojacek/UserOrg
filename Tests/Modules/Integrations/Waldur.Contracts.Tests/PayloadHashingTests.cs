using Waldur.Contracts.Payloads;

namespace Waldur.Contracts.Tests;

/// <summary>
/// Hashing tests against the real payload records — the shapes the producer and the DB rebuilder
/// both hash via <see cref="UserOrg.Shared.Hashing.IIdentityHashed.ComputeIdentityHash"/>.
/// </summary>
public sealed class PayloadHashingTests
{
    private static ProjectPayload SampleProject(bool isRemoved = false, DateTime? createdAt = null) =>
        new(
            ExternalId: "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0",
            Name: "Project Alpha",
            ShortName: "alpha",
            Description: "a project",
            StartDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate: DateTime.MaxValue,
            IsRemoved: isRemoved,
            Resources:
            [
                new ResourcePayload(
                    Name: "res-1",
                    ResourceExternalId: "11111111-1111-1111-1111-111111111111",
                    Description: "resource one",
                    StartDate: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                    EndDate: DateTime.MaxValue,
                    IsDeleted: false,
                    Endpoint: "login.example.org",
                    AggregationName: "HPC offering",
                    AllocationUnitType: "NodeHours",
                    AllocationUnitCount: 100,
                    HeappeUri: "https://heappe.example.org",
                    HeappeIdentifier: "cluster-1",
                    Values: [new ResourceAllocationValuePayload("HEAppELocalBasePath", "/scratch/res-1")])
                {
                    CreatedAt = createdAt,
                    ModifiedAt = createdAt,
                },
            ])
        {
            CreatedAt = createdAt,
        };

    [Fact]
    public void Should_IgnoreAuditTimestamps_When_HashingProject()
    {
        var early = SampleProject(createdAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var late = SampleProject(createdAt: new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc));

        // Proves no clock-derived field enters the hash (ADR-0055/A1): the same entity hashed at
        // two different "times" yields the same hash.
        Assert.Equal(early.ComputeIdentityHash(), late.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ChangeHash_When_IsRemovedFlips()
    {
        var present = SampleProject(isRemoved: false);
        var removed = SampleProject(isRemoved: true);

        Assert.NotEqual(present.ComputeIdentityHash(), removed.ComputeIdentityHash());
    }

    [Fact]
    public void Should_IgnoreResourceOrder_When_HashingProject()
    {
        var resA = SampleProject().Resources[0];
        var resB = resA with { Name = "res-2", ResourceExternalId = "22222222-2222-2222-2222-222222222222" };

        var forward = SampleProject() with { Resources = [resA, resB] };
        var reversed = SampleProject() with { Resources = [resB, resA] };

        Assert.Equal(forward.ComputeIdentityHash(), reversed.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ExcludeCreateOnlyProfileFields_When_HashingMembership()
    {
        var member = new ProjectUserPayload("proj-1", "jdoe", "ProjectMember", HasAccess: true);
        var withProfile = member with
        {
            Email = "jdoe@example.org",
            CreatedAt = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
        };

        // A3: user-profile fields are create-only and must not affect the membership hash.
        Assert.Equal(member.ComputeIdentityHash(), withProfile.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ChangeHash_When_MembershipRoleChanges()
    {
        var member = new ProjectUserPayload("proj-1", "jdoe", "ProjectMember", HasAccess: true);
        var promoted = member with { Role = "ProjectManager" };

        Assert.NotEqual(member.ComputeIdentityHash(), promoted.ComputeIdentityHash());
    }

    [Fact]
    public void Should_MatchKnownVector_When_HashingSampleProject()
    {
        // Regression pin: if the canonical serialization ever changes, this catches it.
        Assert.Equal(KnownVectors.SampleProject, SampleProject().ComputeIdentityHash());
    }
}
