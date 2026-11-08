using UserOrg.ServiceDefaults.Audit;

namespace ServiceDefaults.Tests.Audit;

public sealed class ImmuDbCredentialsTests
{
    [Fact(DisplayName = "Should_RedactThePassword_When_TheCredentialsArePrinted")]
    public void Should_RedactThePassword_When_TheCredentialsArePrinted()
    {
        var printed = new ImmuDbCredentials("immudb", "n0tInTheClearPw", "auditdb").ToString();

        Assert.DoesNotContain("n0tInTheClearPw", printed, StringComparison.Ordinal);
        Assert.Contains("immudb", printed, StringComparison.Ordinal);
        Assert.Contains("auditdb", printed, StringComparison.Ordinal);
    }
}
