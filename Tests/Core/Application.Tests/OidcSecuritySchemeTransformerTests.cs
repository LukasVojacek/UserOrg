using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class OidcSecuritySchemeTransformerTests
{
    private static readonly Uri AuthorizationUrl = new("http://localhost:8080/realms/master/protocol/openid-connect/auth");

    private static readonly Uri TokenUrl = new("http://localhost:8080/realms/master/protocol/openid-connect/token");

    [Fact(DisplayName = "Should_Leave_The_Document_Untouched_When_Oidc_Login_Is_Not_Configured")]
    public async Task ShouldLeaveTheDocumentUntouchedWhenOidcLoginIsNotConfigured()
    {
        var document = DocumentWithOneOperation();
        var sut = new OidcSecuritySchemeTransformer(Options.Create(new ScalarOidcOptions
        {
            AuthorizationUrl = AuthorizationUrl,
            TokenUrl = TokenUrl,
            ClientId = "",
        }));

        await sut.TransformAsync(document, null!, CancellationToken.None);

        Assert.Null(document.Components);
        Assert.Null(OperationOf(document).Security);
    }

    [Fact(DisplayName = "Should_Add_An_Authorization_Code_Scheme_When_Configured")]
    public async Task ShouldAddAnAuthorizationCodeSchemeWhenConfigured()
    {
        var document = DocumentWithOneOperation();
        var sut = new OidcSecuritySchemeTransformer(Options.Create(ConfiguredOptions()));

        await sut.TransformAsync(document, null!, CancellationToken.None);

        var oidcScheme = Assert.IsType<OpenApiSecurityScheme>(document.Components!.SecuritySchemes!["Oidc"]);
        Assert.Equal(SecuritySchemeType.OAuth2, oidcScheme.Type);
        var flow = oidcScheme.Flows?.AuthorizationCode;
        Assert.NotNull(flow);
        Assert.Equal(AuthorizationUrl, flow.AuthorizationUrl);
        Assert.Equal(TokenUrl, flow.TokenUrl);
        Assert.Equal(["openid", "profile"], flow.Scopes!.Keys.Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "Should_Offer_Oidc_As_An_Alternative_To_Bearer")]
    public async Task ShouldOfferOidcAsAnAlternativeToBearer()
    {
        var document = DocumentWithOneOperation();

        await new BearerSecuritySchemeTransformer().TransformAsync(document, null!, CancellationToken.None);
        await new OidcSecuritySchemeTransformer(Options.Create(ConfiguredOptions()))
            .TransformAsync(document, null!, CancellationToken.None);

        var security = OperationOf(document).Security;
        Assert.NotNull(security);
        Assert.Equal(2, security.Count);

        var bearerRequirement = Assert.Single(security[0]);
        Assert.Equal("Bearer", bearerRequirement.Key.Reference.Id);

        var oidcRequirement = Assert.Single(security[1]);
        Assert.Equal("Oidc", oidcRequirement.Key.Reference.Id);
        Assert.Same(document, oidcRequirement.Key.Reference.HostDocument);
        Assert.Equal(["openid", "profile"], oidcRequirement.Value);
    }

    private static ScalarOidcOptions ConfiguredOptions() => new()
    {
        AuthorizationUrl = AuthorizationUrl,
        TokenUrl = TokenUrl,
        ClientId = "scalar",
    };

    private static OpenApiDocument DocumentWithOneOperation() => new()
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

    private static OpenApiOperation OperationOf(OpenApiDocument document) =>
        document.Paths["/test"].Operations![HttpMethod.Get];
}
