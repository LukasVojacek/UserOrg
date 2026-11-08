using Core.Features.ResourceRequest.Flow;

namespace Core.UnitTests.ResourceRequests.Flow;

/// <summary>
/// Unit tests for the single-use PI action-link token helper (ADR-0064): URL-safe minting, SHA-256
/// hash-at-rest, and constant-time verify roundtrip. Shared with B06's public token endpoints.
/// </summary>
public sealed class ResourceRequestTokenTests
{
    [Fact]
    public void Should_MintUrlSafeUniqueTokens_When_Generated()
    {
        var a = ResourceRequestToken.Generate();
        var b = ResourceRequestToken.Generate();

        Assert.NotEqual(a, b);
        Assert.NotEmpty(a);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain('=', a);
    }

    [Fact]
    public void Should_NotStorePlaintext_When_Hashed()
    {
        var token = ResourceRequestToken.Generate();

        var hash = ResourceRequestToken.Hash(token);

        Assert.NotEqual(token, hash);
        Assert.Equal(64, hash.Length); // SHA-256 lower-hex
        Assert.Equal(hash, ResourceRequestToken.Hash(token)); // deterministic
    }

    [Fact]
    public void Should_VerifyRoundtrip_When_TokenMatchesLiveStoredHash()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var token = ResourceRequestToken.Generate();
        var hash = ResourceRequestToken.Hash(token);
        var future = now.AddHours(1);

        Assert.True(ResourceRequestToken.Verify(token, hash, future, now));
        Assert.False(ResourceRequestToken.Verify(ResourceRequestToken.Generate(), hash, future, now));
        Assert.False(ResourceRequestToken.Verify("", hash, future, now));
    }

    [Fact]
    public void Should_FailVerify_When_TokenUsedOrExpired()
    {
        var now = new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        var token = ResourceRequestToken.Generate();
        var hash = ResourceRequestToken.Hash(token);

        // Used: the projection cleared its hash — a previously-valid token can never verify again.
        Assert.False(ResourceRequestToken.Verify(token, expectedHash: null, expiresAt: now.AddHours(1), asOf: now));
        // Expired: TTL elapsed relative to asOf.
        Assert.False(ResourceRequestToken.Verify(token, hash, expiresAt: now.AddHours(-1), asOf: now));
        // Absent expiry.
        Assert.False(ResourceRequestToken.Verify(token, hash, expiresAt: null, asOf: now));
    }
}
