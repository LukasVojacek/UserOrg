using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.InternalToken;
using UserOrg.Shared;

using Xunit;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Pins <see cref="InternalTokenProvider"/>'s <see cref="HybridCache"/> key: (authMethod, subject,
/// systemRole, providerId, projectHash, originalJti). The originalJti segment is what keeps a cached
/// token from carrying a stale original jti after an IdP token refresh inside the cache TTL.
/// </summary>
public sealed class InternalTokenProviderTests
{
    private static Task<string?> GenerateUserTokenAsync(
        IInternalTokenProvider provider,
        string subject,
        string username,
        string systemRole,
        string providerId,
        string? email,
        string? originalJti)
        => provider.GenerateInternalTokenAsync(
            EAuthorizationMethod.JwtBearerToken,
            subject,
            username,
            systemRole,
            providerId,
            email,
            projectRoles: null,
            originalJti: originalJti);

    [Fact]
    public async Task GenerateInternalTokenAsync_DifferentOriginalJti_MintsDistinctTokensCarryingTheirOwnOriginalJti()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var fromFirstIdpToken = await GenerateUserTokenAsync(
            provider, "sub-1", "user-1", "User", "provider-1", "user1@test.local", "idp-jti-1");
        var fromSecondIdpToken = await GenerateUserTokenAsync(
            provider, "sub-1", "user-1", "User", "provider-1", "user1@test.local", "idp-jti-2");

        // Same user, same role, same provider: everything except the incoming token id matches, so the
        // second call may only get its own token if originalJti is part of the cache key.
        Assert.Equal("idp-jti-1", MintedToken.OriginalJtiOf(MintedToken.Read(fromFirstIdpToken)));
        Assert.Equal("idp-jti-2", MintedToken.OriginalJtiOf(MintedToken.Read(fromSecondIdpToken)));
    }

    [Fact]
    public async Task GenerateInternalTokenAsync_SameOriginalJti_ReturnsSameCachedToken()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var first = await GenerateUserTokenAsync(
            provider, "sub-1", "user-1", "User", "provider-1", "user1@test.local", "idp-jti-1");
        var second = await GenerateUserTokenAsync(
            provider, "sub-1", "user-1", "User", "provider-1", "user1@test.local", "idp-jti-1");

        // TokenService stamps a fresh jti/iat on every real mint, so identical strings can only occur if
        // the second call was served from the same HybridCache entry as the first.
        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateInternalTokenAsync_SameSubjectAuthMethodRoleProvider_UsernameAndEmailAreNotKeySegments()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var first = await GenerateUserTokenAsync(
            provider, "sub-2", "user-2", "Admin", "provider-2", "user2@test.local", "idp-jti-2");
        var second = await GenerateUserTokenAsync(
            provider, "sub-2", "user-2-renamed", "Admin", "provider-2", "user2-renamed@test.local", "idp-jti-2");

        // Pins the negative half of the key: username and email must NOT distinguish the entry even
        // though they differ between calls — the key was widened by originalJti and nothing else.
        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task GenerateInternalTokenAsync_DifferentSubject_ReturnsDistinctToken()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var forSubjectOne = await GenerateUserTokenAsync(
            provider, "sub-a", "user-a", "User", "provider-1", email: null, originalJti: "idp-jti-1");
        var forSubjectTwo = await GenerateUserTokenAsync(
            provider, "sub-b", "user-b", "User", "provider-1", email: null, originalJti: "idp-jti-1");

        // Control for the two tests above: a degenerate key that never varied would also make them
        // pass, and would fail here. Together they pin a key that discriminates by subject and
        // nothing per-call.
        Assert.NotEqual(forSubjectOne, forSubjectTwo);
    }

    [Fact]
    public async Task Should_ShareTheCachedToken_When_TheProjectRolesAreNullOrEmpty()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var withNull = await GenerateProjectTokenAsync(provider, projectRoles: null);
        var withEmpty = await GenerateProjectTokenAsync(provider, new Dictionary<string, ICollection<string>>());

        Assert.False(string.IsNullOrEmpty(withNull));
        Assert.Equal(withNull, withEmpty);
    }

    [Fact]
    public async Task Should_ShareTheCachedToken_When_TheSameProjectRolesArriveInADifferentOrder()
    {
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var first = await GenerateProjectTokenAsync(provider, new Dictionary<string, ICollection<string>>
        {
            ["ProjectUser"] = ["alpha", "beta"],
            ["ProjectManager"] = ["gamma"],
        });
        var reordered = await GenerateProjectTokenAsync(provider, new Dictionary<string, ICollection<string>>
        {
            ["ProjectManager"] = ["gamma"],
            ["ProjectUser"] = ["beta", "alpha"],
        });

        Assert.Equal(first, reordered);
    }

    [Theory]
    [InlineData("ProjectUser", "alpha,gamma")]
    [InlineData("ProjectManager", "alpha,beta")]
    [InlineData("ProjectUser", "alpha")]
    public async Task Should_MintADistinctToken_When_TheProjectRolesDiffer(string role, string projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        using var services = await InternalTokenTestServices.BuildStartedAsync();
        var provider = services.GetRequiredService<IInternalTokenProvider>();

        var baseline = await GenerateProjectTokenAsync(provider, new Dictionary<string, ICollection<string>> { ["ProjectUser"] = ["alpha", "beta"] });
        var different = await GenerateProjectTokenAsync(provider, new Dictionary<string, ICollection<string>> { [role] = projects.Split(',') });
        var withoutProjects = await GenerateProjectTokenAsync(provider, projectRoles: null);

        Assert.NotEqual(baseline, different);
        Assert.NotEqual(baseline, withoutProjects);
    }

    private static Task<string?> GenerateProjectTokenAsync(IInternalTokenProvider provider, IDictionary<string, ICollection<string>>? projectRoles)
        => provider.GenerateInternalTokenAsync(
            EAuthorizationMethod.JwtBearerToken,
            "sub-projects",
            "user-projects",
            "User",
            "provider-1",
            email: null,
            projectRoles: projectRoles,
            originalJti: "idp-jti-projects");
}
