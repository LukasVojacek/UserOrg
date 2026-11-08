using Microsoft.AspNetCore.Http;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// In Normal mode a request to a public route whose path segment is a credential, in any casing, leaves the token in
/// no captured event: not the request summary, its route values or the Warning completion event of a 4xx.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiNormalModeTokenPathTests
{
    private const string Token = "abc123";

    public static TheoryData<string, string> TokenRequests => new()
    {
        { HttpMethods.Post, "/api/v2/public/ACTIONLINKS/abc123" },
        { HttpMethods.Get, "/API/V2/Public/Requests/BYLINK/abc123" },
        { HttpMethods.Get, "/api/v2/PUBLIC/resourcerequestactions/abc123" },
        { HttpMethods.Post, "/Api/V2/Public/ResourceRequestActions/abc123" },
        { HttpMethods.Post, "/api/public/Handle/abc123" },
        { HttpMethods.Post, "/API/Action/handle/abc123" },
    };

    [Theory]
    [MemberData(nameof(TokenRequests))]
    public async Task Should_LeaveTheTokenInNoCapturedEvent_When_APublicTokenRouteIsRequestedInNormalMode(string method, string path)
    {
        await using var host = LogCaptureApiFactory.Start();

        await host.SendAsync(method, path);
        await host.SendSentinelAndAwaitItsSummaryAsync();

        host.Events.AssertNoneMentions(Token);
    }
}