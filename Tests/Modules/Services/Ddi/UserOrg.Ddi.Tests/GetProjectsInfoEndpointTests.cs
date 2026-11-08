using Core.DataAccess.Queries.Contracts;

using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using UserOrg.Ddi.Features.Service;

using Xunit;

using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace UserOrg.Ddi.Tests;

public sealed class GetProjectsInfoEndpointTests
{
    [Fact]
    public async Task Should_Map_Views_To_Response_When_Query_Returns_Data()
    {
        var view = new ProviderResourcesView(
            "prov-1",
            "Acme HPC",
            "provider desc",
            [
                new ProjectResourceInfoView(
                    Guid.NewGuid(),
                    "gpu-res",
                    "res desc",
                    null,
                    null,
                    [new AssociatedProjectView(Guid.NewGuid(), "Proj", "PRJ", "PRJ|Acme_HPC|gpu-res", null, null)],
                    [new ResourceAssignmentInfoView("loc", "locdesc", "HPC", 10, EAllocationUnitType.CoreHours, "agg", [new SpecificationView("k", "v")])])
            ]);

        var query = new Mock<IDdiProjectsInfoQuery>();
        query.Setup(q => q.GetProjectsInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { view });

        var result = await GetProjectsInfoEndpoint.HandleAsync(query.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<List<GetProjectsInfoEndpoint.Response>>>(result);
        var provider = Assert.Single(ok.Value!);
        Assert.Equal("prov-1", provider.ProviderId);
        Assert.Equal("Acme HPC", provider.ProviderName);

        var resource = Assert.Single(provider.Resources);
        Assert.Equal("gpu-res", resource.Name);

        var associated = Assert.Single(resource.AssociatedProjects);
        Assert.Equal("PRJ|Acme_HPC|gpu-res", associated.CollectorName);

        var assignment = Assert.Single(resource.Assignments);
        Assert.Equal("HPC", assignment.ResourceType);
        Assert.Equal(EAllocationUnitType.CoreHours, assignment.AllocationUnitType);

        var spec = Assert.Single(assignment.Specifications);
        Assert.Equal("k", spec.Key);
        Assert.Equal("v", spec.Value);
    }

    [Fact]
    public async Task Should_Return_Empty_When_Query_Returns_Nothing()
    {
        var query = new Mock<IDdiProjectsInfoQuery>();
        query.Setup(q => q.GetProjectsInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ProviderResourcesView>());

        var result = await GetProjectsInfoEndpoint.HandleAsync(query.Object, CancellationToken.None);

        var ok = Assert.IsType<Ok<List<GetProjectsInfoEndpoint.Response>>>(result);
        Assert.Empty(ok.Value!);
    }
}
