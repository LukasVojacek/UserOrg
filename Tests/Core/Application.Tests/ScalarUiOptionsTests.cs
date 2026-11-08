using UserOrganizationManagement.Domain.Configuration;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ScalarUiOptionsTests
{
    [Fact(DisplayName = "Should_Use_Defaults_And_Normalize_Scalar_Overrides")]
    public void ShouldUseDefaultsAndNormalizeScalarOverrides()
    {
        var options = new ScalarUiOptions
        {
            PublicPathBase = "/userorg/",
            RoutePrefix = "/docs/",
            ServerUrl = "https://gateway.example.local/",
        };

        Assert.Equal("UserOrg API", options.Title);
        Assert.Equal("UserOrg Management API", options.Description);
        Assert.Equal("userorg", options.PublicPathBase);
        Assert.Equal("docs", options.RoutePrefix);
        Assert.Equal("https://gateway.example.local", options.ServerUrl);
    }
}
