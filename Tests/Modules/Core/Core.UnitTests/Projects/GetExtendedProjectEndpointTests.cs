using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.GetExtendedProject;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.UnitTests.Projects;

public sealed class GetExtendedProjectEndpointTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private static Caller CallerInScope() => new(
        new IdpId("u"), "Owner", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller CallerOutOfScope() => new(
        new IdpId("u"), "Owner", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ExtendedProjectSummary Sample(IReadOnlyList<AssignedResourceData>? resources = null, Description? description = null, IReadOnlyList<TagName>? tags = null) => new(
        ShortName, ProjectName.Create("Project One"), description,
        Email.Create("p@example.org"),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        DomainTypeName.Create("Biology"), tags ?? [], resources ?? []);

    private static async Task<IResult> InvokeAsync(Caller caller, ExtendedProjectSummary? project)
    {
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetExtendedProjectAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        return await GetExtendedProjectEndpoint.HandleAsync(ShortName, caller, query.Object, CancellationToken.None);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNotFound_When_CallerOutOfScope()
    {
        var result = await InvokeAsync(CallerOutOfScope(), Sample());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ReturnNotFound_When_ProjectNotFound()
    {
        var result = await InvokeAsync(CallerInScope(), project: null);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
    }

    [Fact]
    public async Task Should_ExposeTagNames_When_TagsAssigned()
    {
        var result = await InvokeAsync(CallerInScope(), Sample(tags: [TagName.Create("biology")]));

        var ok = Assert.IsType<Ok<GetExtendedProjectEndpoint.Response>>(result);
        Assert.Equal("biology", Assert.Single(ok.Value!.AssignedTags));
    }

    [Fact]
    public async Task Should_ReturnOk_And_MapEmptyDescriptionToNull_When_NoResources()
    {
        var result = await InvokeAsync(CallerInScope(), Sample());

        var ok = Assert.IsType<Ok<GetExtendedProjectEndpoint.Response>>(result);
        Assert.Equal("PROJ-1", ok.Value!.ShortName);
        Assert.Equal("Biology", ok.Value.DomainTypeName);
        Assert.Null(ok.Value.Description);
        Assert.Empty(ok.Value.AssignedResources);
    }

    [Fact]
    public async Task Should_MapResourcesAndSpecifications_When_ResourcesAssigned()
    {
        var resource = new AssignedResourceData(
            "hpc-1", "resource desc", "loc-1", ELocationType.HPC, 100, EAllocationUnitType.CoreHours, "agg",
            [("queue", "long")]);

        var result = await InvokeAsync(CallerInScope(), Sample(resources: [resource], description: Description.Create("desc")));

        var ok = Assert.IsType<Ok<GetExtendedProjectEndpoint.Response>>(result);
        Assert.Equal("desc", ok.Value!.Description);
        var mapped = Assert.Single(ok.Value.AssignedResources);
        Assert.Equal("hpc-1", mapped.ResourceName);
        Assert.Equal(ELocationType.HPC, mapped.LocationType);
        Assert.Equal(EAllocationUnitType.CoreHours, mapped.AllocationUnitType);
        var spec = Assert.Single(mapped.Specifications);
        Assert.Equal("queue", spec.Name);
        Assert.Equal("long", spec.Value);
    }
}
