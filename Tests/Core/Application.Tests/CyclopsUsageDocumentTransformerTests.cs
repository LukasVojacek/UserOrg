using Microsoft.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// F2 C1: CyclopsUsage is Gateway-served, not a Core route, so it is injected into the v2-User OpenAPI
/// document purely for Scalar discoverability. Verifies the transformer adds the path, GET operation,
/// route/query parameters and responses.
/// </summary>
public sealed class CyclopsUsageDocumentTransformerTests
{
    private const string Path = "/api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}";
    private const string TotalUsagePath = "/api/Cyclops/TotalUsage/{ProviderResourceName}";

    [Fact(DisplayName = "Should_Inject_CyclopsUsage_Get_Operation_Into_Document")]
    public async Task ShouldInjectCyclopsUsageGetOperation()
    {
        var document = new OpenApiDocument();
        var sut = new CyclopsUsageDocumentTransformer();

        await sut.TransformAsync(document, null!, CancellationToken.None);

        var pathItem = Assert.Contains(Path, document.Paths);
        var operation = pathItem.Operations![HttpMethod.Get];

        Assert.Contains("ProjectShortName", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("ProviderResourceName", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("fromDate", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("toDate", operation.Parameters!.Select(p => p.Name));

        Assert.True(operation.Responses!.ContainsKey("200"));
        Assert.True(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses!.ContainsKey("401"));
        Assert.True(operation.Responses!.ContainsKey("403"));
        Assert.True(operation.Responses!.ContainsKey("500"));
    }

    [Fact(DisplayName = "Should_Inject_CyclopsTotalUsage_Get_Operation_Into_Document")]
    public async Task ShouldInjectCyclopsTotalUsageGetOperation()
    {
        var document = new OpenApiDocument();
        var sut = new CyclopsUsageDocumentTransformer();

        await sut.TransformAsync(document, null!, CancellationToken.None);

        var pathItem = Assert.Contains(TotalUsagePath, document.Paths);
        var operation = pathItem.Operations![HttpMethod.Get];

        // The aggregate endpoint spans every project, so it takes no ProjectShortName.
        Assert.DoesNotContain("ProjectShortName", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("ProviderResourceName", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("fromDate", operation.Parameters!.Select(p => p.Name));
        Assert.Contains("toDate", operation.Parameters!.Select(p => p.Name));

        Assert.True(operation.Responses!.ContainsKey("200"));
        Assert.True(operation.Responses!.ContainsKey("400"));
        Assert.True(operation.Responses!.ContainsKey("401"));
        Assert.True(operation.Responses!.ContainsKey("403"));
        Assert.True(operation.Responses!.ContainsKey("500"));
    }
}
