using System.Security.Claims;

using UserOrg.Shared.Logging;

namespace UserOrg.Gateway.Tests.Logging;

public sealed class ClaimLogFormatterTests
{
    private const string LongRoleType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    private static readonly string[] AllowListedTypes =
        ["sub", "jti", "original_jti", "role", LongRoleType, "projectRoles", "am", "provider_id", "scope", "aud", "iss", "exp"];

    public static TheoryData<string> ReadableTypes => [.. AllowListedTypes];

    public static TheoryData<string> RedactedTypes => ["email", "preferred_username", "name", "given_name"];

    [Theory]
    [MemberData(nameof(ReadableTypes))]
    public void Should_PrintTheValue_When_TheClaimTypeIsAllowListed(string claimType)
    {
        var formatted = ClaimLogFormatter.Format([new Claim(claimType, "readable-value")]);

        Assert.Equal($"{claimType}=readable-value", formatted);
    }

    [Theory]
    [MemberData(nameof(RedactedTypes))]
    public void Should_PrintRedacted_When_TheClaimTypeIsNotAllowListed(string claimType)
    {
        var formatted = ClaimLogFormatter.Format([new Claim(claimType, "personal-value")]);

        Assert.Equal($"{claimType}=[REDACTED]", formatted);
    }

    [Fact]
    public void Should_KeepSubReadableAndRedactTheEmail_When_BothAreFormattedTogether()
    {
        var formatted = ClaimLogFormatter.Format([new Claim("sub", "sub-1"), new Claim("email", "person@example.org")]);

        Assert.Contains("sub=sub-1", formatted, StringComparison.Ordinal);
        Assert.Contains("email=[REDACTED]", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("person@example.org", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_AllowExactlyTheDesignedClaimTypes_When_TheAllowListIsRead()
    {
        Assert.True(ClaimLogFormatter.ReadableClaimTypes.SetEquals(AllowListedTypes));
    }
}