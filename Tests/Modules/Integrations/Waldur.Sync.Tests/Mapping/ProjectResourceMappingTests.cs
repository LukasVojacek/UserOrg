using Waldur.Sync.Api.WaldurApiModels;
using Waldur.Sync.Configuration;
using Waldur.Sync.Mapping;

using Xunit;

namespace Waldur.Sync.Tests.Mapping;

public sealed class ProjectResourceMappingTests
{
    private static ResourceOffering Ro(
        string? effectiveId = "eff-a",
        string? backendId = "back-a",
        string state = "active",
        Guid? resourceId = null,
        IReadOnlyDictionary<string, decimal>? allocations = null,
        DateTimeOffset? created = null,
        DateTimeOffset? endDate = null,
        ApiPublicOffering? offering = null) =>
        new(
            MapperTestData.Resource(id: resourceId, effectiveId: effectiveId, backendId: backendId, state: state,
                allocations: allocations, created: created, endDate: endDate),
            offering ?? MapperTestData.Offering());

    [Fact]
    public void Should_NameResource_ByEffectiveId_When_Present()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: "eff-x", backendId: "back-x")]);

        Assert.Equal("eff-x", Assert.Single(result.Payload!.Resources).Name);
    }

    [Fact]
    public void Should_NameResource_ByBackendId_When_NoEffectiveId()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: null, backendId: "back-only")]);

        Assert.Equal("back-only", Assert.Single(result.Payload!.Resources).Name);
    }

    [Fact]
    public void Should_GroupResources_ByEffectiveBackendCustomer_And_AggregateDates()
    {
        var mapper = MapperTestData.Mapper();
        var early = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var late = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var resources = new[]
        {
            Ro(resourceId: Guid.NewGuid(), created: early, endDate: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Ro(resourceId: Guid.NewGuid(), created: new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero), endDate: late)
        };

        var result = mapper.MapProject(MapperTestData.Project(), [.. resources]);

        // Same group (same effective/backend/customer) → two payloads sharing name + aggregated dates.
        Assert.Equal(2, result.Payload!.Resources.Count);
        Assert.All(result.Payload.Resources, r => Assert.Equal(early.UtcDateTime, r.StartDate));
        Assert.All(result.Payload.Resources, r => Assert.Equal(late.UtcDateTime, r.EndDate));
    }

    [Fact]
    public void Should_FallBackEndDate_ToMaxValue_When_Null()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(endDate: null)]);

        Assert.Equal(DateTime.MaxValue, Assert.Single(result.Payload!.Resources).EndDate);
    }

    [Fact]
    public void Should_MarkResourceDeleted_When_AllTerminated()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(state: "terminated")]);

        Assert.True(Assert.Single(result.Payload!.Resources).IsDeleted);
    }

    [Fact]
    public void Should_SkipResource_When_NoAllocations()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(
            MapperTestData.Project(),
            [Ro(allocations: new Dictionary<string, decimal>())]);

        Assert.Null(result.Payload); // project becomes ineligible (no eligible resources)
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ResourceNoAllocations);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProjectNoEligibleResources);
    }

    [Fact]
    public void Should_SkipResource_When_NoEffectiveOrBackendId()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: null, backendId: null)]);

        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ResourceNoBackendOrEffectiveId);
    }

    [Fact]
    public void Should_SkipResource_When_OfferingMissing()
    {
        var mapper = MapperTestData.Mapper();
        var ro = new ResourceOffering(MapperTestData.Resource(), Offering: null);

        var result = mapper.MapProject(MapperTestData.Project(), [ro]);

        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ResourceOfferingMissing);
    }

    [Fact]
    public void Should_SkipResource_When_OfferingHasNoSshEndpoint()
    {
        var mapper = MapperTestData.Mapper();
        var offering = MapperTestData.Offering(endpoints: [new ApiPublicOfferingEndpoint(Guid.NewGuid(), "Web", "https://x")]);

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(offering: offering)]);

        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ResourceNoSshEndpoint);
    }

    [Fact]
    public void Should_EmitRemovedProject_WithZeroResources_ExemptFromEligibility()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(isRemoved: true), []);

        Assert.NotNull(result.Payload);
        Assert.True(result.Payload!.IsRemoved);
        Assert.Empty(result.Payload.Resources);
    }

    [Fact]
    public void Should_SkipProject_When_NoEligibleResources_AndNotRemoved()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), []);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProjectNoEligibleResources);
    }

    [Fact]
    public void Should_SkipProject_When_CreatedMissing()
    {
        var mapper = MapperTestData.Mapper();
        var project = new ApiProject(MapperTestData.ProjectId, "n", "s", "d", null, null, null, false);

        var result = mapper.MapProject(project, [Ro()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProjectCreatedMissing);
    }

    [Fact]
    public void Should_TruncateProjectName_ToDbLimit()
    {
        var mapper = MapperTestData.Mapper();
        var longName = new string('a', 250);

        var result = mapper.MapProject(MapperTestData.Project(name: longName), [Ro()]);

        Assert.Equal(100, result.Payload!.Name.Length);
    }

    [Fact]
    public void Should_FallBackProjectStartDate_ToCreated_When_Null()
    {
        var mapper = MapperTestData.Mapper();
        var created = new DateTimeOffset(2021, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var result = mapper.MapProject(MapperTestData.Project(created: created, startDate: null), [Ro()]);

        Assert.Equal(created.UtcDateTime, result.Payload!.StartDate);
    }

    [Fact]
    public void Should_SetHeappeUriAndIdentifier_When_ProviderInMap()
    {
        var heappe = new Dictionary<string, WaldurHeappeInstanceOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [MapperTestData.CustomerId.ToString("N")] = new() { Uri = new Uri("https://heappe.example"), Identifier = "HP-1" }
        };
        var mapper = MapperTestData.Mapper(heappe: heappe);

        var result = mapper.MapProject(MapperTestData.Project(), [Ro()]);

        var resource = Assert.Single(result.Payload!.Resources);
        Assert.Equal("https://heappe.example/", resource.HeappeUri); // trailing slash normalized
        Assert.Equal("HP-1", resource.HeappeIdentifier);
    }

    [Fact]
    public void Should_SkipResource_When_ProviderNotInHeappeMap()
    {
        // A resource whose offering customer has no HEAppE mapping cannot be exported: HEAppE
        // provisioning requires a URI + identifier, so the resource is skipped instead of being
        // mapped with null Heappe fields.
        var mapper = MapperTestData.Mapper(heappe: new Dictionary<string, WaldurHeappeInstanceOptions>(StringComparer.OrdinalIgnoreCase));

        var result = mapper.MapProject(MapperTestData.Project(), [Ro()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ResourceMappingMissing);
    }

    [Fact]
    public void Should_ProduceSamePayload_ForEfpAndPuhuri_GivenSameData()
    {
        var efp = MapperTestData.Mapper(serviceName: "EFP-Waldur");
        var puhuri = MapperTestData.Mapper(serviceName: "Puhuri-Waldur");

        var efpResult = efp.MapProject(MapperTestData.Project(), [Ro()]);
        var puhuriResult = puhuri.MapProject(MapperTestData.Project(), [Ro()]);

        // ServiceName is stamped on the event, not the payload — identical data ⇒ identical hash.
        Assert.Equal(
            efpResult.Payload!.ComputeIdentityHash(),
            puhuriResult.Payload!.ComputeIdentityHash());
    }

    [Fact]
    public void Should_ProduceStableHash_AcrossRepeatedMappings_NoClock()
    {
        var mapper = MapperTestData.Mapper();

        var first = mapper.MapProject(MapperTestData.Project(), [Ro()]);
        var second = mapper.MapProject(MapperTestData.Project(), [Ro()]);

        // A pure, clock-free mapper must hash identically every time (ADR-0055/A1 — no Updated storm).
        Assert.Equal(
            first.Payload!.ComputeIdentityHash(),
            second.Payload!.ComputeIdentityHash());
    }

    [Fact]
    public void Should_KeepControlCharacters_InResourceName()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: "eff\u0007-x", backendId: "back-x")]);

        // D11: the consumer matches a resource by (Name, ServiceName), so rewriting the mapped name
        // re-keys it — a second row is inserted and the stored one is orphaned instead of updated.
        Assert.Equal("eff-x", Assert.Single(result.Payload!.Resources).Name);
    }

    [Fact]
    public void Should_TruncateResourceName_ToDbLimit()
    {
        var mapper = MapperTestData.Mapper();
        var longEffectiveId = $"eff-x{new string('x', 100)}";

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: longEffectiveId, backendId: "back-x")]);

        Assert.Equal(longEffectiveId[..100], Assert.Single(result.Payload!.Resources).Name);
    }

    [Fact]
    public void Should_MapResource_When_EffectiveIdIsOnlyControlCharacters()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(), [Ro(effectiveId: "\u0007\u0007", backendId: null)]);

        Assert.Null(result.Payload);
    }

    [Theory]
    [InlineData("slug-with-diacritics-é")]
    [InlineData("slug with spaces")]
    public void Should_SkipProject_When_SlugInvalid(string slug)
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(slug: slug), [Ro()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProjectSlugInvalidOrMissing);
    }

    [Fact]
    public void Should_SkipProject_When_SlugTooLong()
    {
        var mapper = MapperTestData.Mapper();
        var slug = new string('a', 51);

        var result = mapper.MapProject(MapperTestData.Project(slug: slug), [Ro()]);

        Assert.Null(result.Payload);
        Assert.Contains(result.Skips, s => s.Reason == EMappingSkipReason.ProjectSlugInvalidOrMissing);
    }

    [Fact]
    public void Should_MapProject_When_SlugConforms()
    {
        var mapper = MapperTestData.Mapper();

        var result = mapper.MapProject(MapperTestData.Project(slug: "valid-slug_123"), [Ro()]);

        Assert.Equal("valid-slug_123", result.Payload!.ShortName);
    }
}
