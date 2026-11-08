using Microsoft.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class OperationIdDocumentTransformerTests
{
    private static OpenApiDocument DocumentWith(params (string Path, HttpMethod Method, string? OperationId)[] operations)
    {
        var paths = new OpenApiPaths();

        foreach (var (path, method, operationId) in operations)
        {
            if (!paths.TryGetValue(path, out var pathItem))
            {
                pathItem = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation>() };
                paths[path] = pathItem;
            }

            pathItem.Operations![method] = new OpenApiOperation { OperationId = operationId };
        }

        return new OpenApiDocument { Paths = paths };
    }

    private static string IdOf(OpenApiDocument document, string path, HttpMethod method) =>
        document.Paths[path].Operations![method].OperationId!;

    [Fact(DisplayName = "Should_Derive_OperationId_From_Method_And_Route_Template")]
    public async Task ShouldDeriveOperationIdFromMethodAndRouteTemplate()
    {
        var document = DocumentWith(
            ("/api/Enums/RequestStates", HttpMethod.Get, null),
            ("/api/Provider/{Id:GUID}", HttpMethod.Delete, null),
            ("/api/Project/{ProjectShortName}/User/{IdpUserId}", HttpMethod.Post, null));

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        Assert.Equal("GetApiEnumsRequestStates", IdOf(document, "/api/Enums/RequestStates", HttpMethod.Get));
        Assert.Equal("DeleteApiProviderById", IdOf(document, "/api/Provider/{Id:GUID}", HttpMethod.Delete));
        Assert.Equal(
            "PostApiProjectByProjectShortNameUserByIdpUserId",
            IdOf(document, "/api/Project/{ProjectShortName}/User/{IdpUserId}", HttpMethod.Post));
    }

    [Fact(DisplayName = "Should_Emit_Only_Alphanumeric_OperationIds")]
    public async Task ShouldEmitOnlyAlphanumericOperationIds()
    {
        var document = DocumentWith(
            ("/.well-known/openid-configuration", HttpMethod.Get, null),
            ("/api/v2/Project/{shortName}", HttpMethod.Put, null));

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        foreach (var pathItem in document.Paths.Values)
        {
            foreach (var operation in pathItem.Operations!.Values)
            {
                Assert.NotNull(operation.OperationId);
                Assert.True(
                    operation.OperationId.All(char.IsLetterOrDigit),
                    $"operationId '{operation.OperationId}' is not a valid client-codegen method name.");
            }
        }
    }

    [Fact(DisplayName = "Should_Never_Overwrite_An_Explicit_OperationId")]
    public async Task ShouldNeverOverwriteAnExplicitOperationId()
    {
        var document = DocumentWith(
            ("/api/Provider/{Id}", HttpMethod.Get, "GetProvider"),
            ("/api/Provider", HttpMethod.Get, null));

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        Assert.Equal("GetProvider", IdOf(document, "/api/Provider/{Id}", HttpMethod.Get));
    }

    [Fact(DisplayName = "Should_Not_Collide_With_An_Existing_Explicit_OperationId")]
    public async Task ShouldNotCollideWithAnExistingExplicitOperationId()
    {
        // The explicit id on the second route is exactly what the first route would generate.
        var document = DocumentWith(
            ("/api/Provider", HttpMethod.Get, null),
            ("/api/Something", HttpMethod.Get, "GetApiProvider"));

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        // The explicit id is kept verbatim, and the generated one steps aside rather than shadowing it.
        Assert.Equal("GetApiProvider", IdOf(document, "/api/Something", HttpMethod.Get));
        Assert.Equal("GetApiProvider2", IdOf(document, "/api/Provider", HttpMethod.Get));
    }

    [Fact(DisplayName = "Should_Disambiguate_Two_Generated_Ids_That_Collide")]
    public async Task ShouldDisambiguateTwoGeneratedIdsThatCollide()
    {
        // "{id}" and the literal segment "ById" both normalise to "ById".
        var document = DocumentWith(
            ("/api/Thing/{id}", HttpMethod.Get, null),
            ("/api/Thing/ById", HttpMethod.Get, null));

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        var first = IdOf(document, "/api/Thing/ById", HttpMethod.Get);
        var second = IdOf(document, "/api/Thing/{id}", HttpMethod.Get);

        Assert.NotEqual(first, second);
        // Ordinal sort over the route template decides which one keeps the bare id: "/api/Thing/ById"
        // sorts before "/api/Thing/{id}".
        Assert.Equal("GetApiThingById", first);
        Assert.Equal("GetApiThingById2", second);
    }

    [Fact(DisplayName = "Should_Assign_The_Same_Ids_Regardless_Of_Insertion_Order")]
    public async Task ShouldAssignTheSameIdsRegardlessOfInsertionOrder()
    {
        (string, HttpMethod, string?)[] entries =
        [
            ("/api/Thing/{id}", HttpMethod.Get, null),
            ("/api/Thing/ById", HttpMethod.Get, null),
            ("/api/Thing", HttpMethod.Post, null),
            ("/api/Thing", HttpMethod.Get, null),
        ];

        var forward = DocumentWith(entries);
        var reversed = DocumentWith([.. entries.Reverse()]);

        await new OperationIdDocumentTransformer().TransformAsync(forward, null!, CancellationToken.None);
        await new OperationIdDocumentTransformer().TransformAsync(reversed, null!, CancellationToken.None);

        foreach (var (path, method, _) in entries)
        {
            Assert.Equal(IdOf(forward, path, method), IdOf(reversed, path, method));
        }
    }

    [Fact(DisplayName = "Should_Leave_An_Empty_Document_Alone")]
    public async Task ShouldLeaveAnEmptyDocumentAlone()
    {
        var document = new OpenApiDocument();

        await new OperationIdDocumentTransformer().TransformAsync(document, null!, CancellationToken.None);

        Assert.True(document.Paths is null || document.Paths.Count == 0);
    }
}
