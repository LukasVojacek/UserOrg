using Microsoft.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class UnversionedHeappeTagDocumentTransformerTests
{
    [Fact(DisplayName = "Should_Describe_Tag_Carried_By_Heappe_Operation_As_Unversioned")]
    public async Task ShouldDescribeHeappeTag()
    {
        var document = Document(("/api/Heappe/KillSwitch", "KillSwitch"), ("/api/Project", "Project"));

        await new UnversionedHeappeTagDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        var description = Tag(document, "KillSwitch").Description;
        Assert.NotNull(description);
        Assert.Contains("unversioned", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/api/v2", description, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_Leave_Tag_Carried_Only_By_Other_Paths_Without_Description")]
    public async Task ShouldLeaveOtherTagUndescribed()
    {
        var document = Document(("/api/Heappe/CommandTemplates", "Heappe"), ("/api/Project", "Project"));

        await new UnversionedHeappeTagDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        Assert.Null(Tag(document, "Project").Description);
    }

    [Fact(DisplayName = "Should_Leave_Document_Without_Heappe_Path_Unchanged")]
    public async Task ShouldLeaveDocumentWithoutHeappePathUnchanged()
    {
        var document = Document(("/api/Project", "Project"), ("/api/HeappeLike", "Heappe"));

        await new UnversionedHeappeTagDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        Assert.All(document.Tags!, tag => Assert.Null(tag.Description));
    }

    private static OpenApiDocument Document(params (string Path, string Tag)[] operations)
    {
        var document = new OpenApiDocument
        {
            Tags = operations.Select(o => new OpenApiTag { Name = o.Tag }).ToHashSet(),
            Paths = [],
        };

        foreach (var (path, tag) in operations)
        {
            document.Paths[path] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Get] = new() { Tags = new HashSet<OpenApiTagReference> { new(tag, document) } },
                },
            };
        }

        return document;
    }

    private static OpenApiTag Tag(OpenApiDocument document, string name) =>
        document.Tags!.Single(tag => tag.Name == name);
}
