using Microsoft.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class BearerSecuritySchemeTransformerTests
{
    [Fact(DisplayName = "Should_Use_Http_Bearer_Security_Scheme_For_Scalar")]
    public async Task ShouldUseHttpBearerSecuritySchemeForScalar()
    {
        var document = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = new OpenApiOperation()
                    }
                }
            }
        };

        var sut = new BearerSecuritySchemeTransformer();

        await sut.TransformAsync(document, null!, CancellationToken.None);

        var bearerScheme = Assert.IsType<OpenApiSecurityScheme>(document.Components!.SecuritySchemes!["Bearer"]);
        Assert.Equal(SecuritySchemeType.Http, bearerScheme.Type);
        Assert.Equal("bearer", bearerScheme.Scheme);
        Assert.Equal("JWT", bearerScheme.BearerFormat);
        Assert.True(
            bearerScheme.Description?.Contains("Bearer prefix is applied automatically", StringComparison.Ordinal) == true);

        var pathItem = document.Paths["/test"];
        Assert.NotNull(pathItem.Operations);

        var operation = pathItem.Operations[HttpMethod.Get];
        Assert.NotNull(operation.Security);
        Assert.Single(operation.Security);
    }
}
