using UserOrganizationManagement.API.OpenApi;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ScalarOidcOptionsTests
{
    private static readonly Uri AuthorizationUrl = new("http://localhost:8080/realms/master/protocol/openid-connect/auth");

    private static readonly Uri TokenUrl = new("http://localhost:8080/realms/master/protocol/openid-connect/token");

    public static TheoryData<Uri?, Uri?, string> IncompleteSettings => new()
    {
        { null, TokenUrl, "testClient" },
        { AuthorizationUrl, null, "testClient" },
        { AuthorizationUrl, TokenUrl, " " },
    };

    [Theory(DisplayName = "Should_Not_Be_Configured_When_A_Part_Is_Missing")]
    [MemberData(nameof(IncompleteSettings))]
    public void ShouldNotBeConfiguredWhenAPartIsMissing(Uri? authorizationUrl, Uri? tokenUrl, string clientId)
    {
        var options = new ScalarOidcOptions
        {
            AuthorizationUrl = authorizationUrl,
            TokenUrl = tokenUrl,
            ClientId = clientId,
        };

        Assert.False(options.IsConfigured);
    }

    [Fact(DisplayName = "Should_Be_Configured_When_Authorization_Url_Token_Url_And_Client_Id_Are_Set")]
    public void ShouldBeConfiguredWhenAuthorizationUrlTokenUrlAndClientIdAreSet()
    {
        var options = new ScalarOidcOptions
        {
            AuthorizationUrl = AuthorizationUrl,
            TokenUrl = TokenUrl,
            ClientId = "scalar",
        };

        Assert.True(options.IsConfigured);
    }
}
