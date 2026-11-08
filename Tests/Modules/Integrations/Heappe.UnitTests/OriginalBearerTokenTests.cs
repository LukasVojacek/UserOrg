using Microsoft.AspNetCore.Http;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

public sealed class OriginalBearerTokenTests
{
    private static async Task<OriginalBearerToken?> BindWithHeaderAsync(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers[OriginalBearerToken.HeaderName] = headerValue;

        return await OriginalBearerToken.BindAsync(context);
    }

    [Fact]
    public async Task Should_Return_Token_When_Header_Has_Bearer_Prefix()
    {
        var result = await BindWithHeaderAsync("Bearer abc.def.ghi");

        Assert.Equal(new OriginalBearerToken("abc.def.ghi"), result);
    }

    [Fact]
    public async Task Should_Return_Null_When_Header_Missing()
    {
        var result = await BindWithHeaderAsync(null);

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_Return_Null_When_Scheme_Is_Not_Bearer()
    {
        var result = await BindWithHeaderAsync("Basic abc.def.ghi");

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_Return_Null_When_Token_Is_Empty_Or_Whitespace()
    {
        var result = await BindWithHeaderAsync("Bearer    ");

        Assert.Null(result);
    }

    [Fact]
    public async Task Should_Return_Token_When_Prefix_Case_Differs()
    {
        var result = await BindWithHeaderAsync("bearer abc.def.ghi");

        Assert.Equal(new OriginalBearerToken("abc.def.ghi"), result);
    }

    [Fact]
    public void Should_Redact_Value_When_ToString_Called()
    {
        const string secret = "abc.def.ghi";
        var token = new OriginalBearerToken(secret);

        Assert.DoesNotContain(secret, token.ToString());
    }
}
