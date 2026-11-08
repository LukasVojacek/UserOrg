using Heappe.Features.Connection;
using Heappe.Features.Nexus;
using Heappe.UnitTests.Features.Nexus;

using Lukdrasil.Result;

using UserOrg.Shared;

using Xunit;

using static Heappe.UnitTests.Features.Connection.TokenExchangeValues;

namespace Heappe.UnitTests.Features.Connection;

/// <summary>
/// The exchanger registered when the token exchange flag is off at startup: a user call to HEAppE carries the
/// caller's token unchanged, whatever the route lists, and nothing calls the IdP (TokenDelegation is not even
/// registered then). These are the flag-off cases that used to live in <see cref="HeappeTokenExchangerTests"/>.
/// </summary>
public sealed class PassThroughTokenExchangerTests
{
    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_callers_token_unchanged()
    {
        var result = await new PassThroughTokenExchanger().ExchangeAsync(Route(HeappeClientId, ["openid", "heappe"]), CallerToken, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(State.Ok, result.State);
        Assert.Equal(CallerToken, result.Value);
    }

    [Fact]
    public async Task Ignores_a_route_without_a_keycloak_client()
    {
        var result = await new PassThroughTokenExchanger().ExchangeAsync(Route(clientId: null, scopes: []), CallerToken, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(CallerToken, result.Value);
    }

    [Theory]
    [InlineData("caller-token-7f3a")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.c2lnbmF0dXJl")]
    public async Task Returns_every_token_byte_for_byte(string bearerToken)
    {
        var result = await new PassThroughTokenExchanger().ExchangeAsync(Route(HeappeClientId, ["openid", "heappe"]), bearerToken, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(bearerToken, result.Value);
    }

    private static HeappeRoute Route(string? clientId, IReadOnlyList<string> scopes) =>
        new(HeappeMock, NexusRegistryFake.HeappeMockProxyBase, clientId, scopes);
}
