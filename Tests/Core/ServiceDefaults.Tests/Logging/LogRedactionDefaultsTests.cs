using System.Diagnostics;
using System.Text.RegularExpressions;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The redaction patterns both hosts share (ADR 0112): what each one removes, what it leaves, and that none of
/// them backtracks on a long client-controlled value.
/// </summary>
public sealed class LogRedactionDefaultsTests
{
    private const double MedianBudgetMilliseconds = 50;
    private static readonly string TokenRun = string.Concat(Enumerable.Repeat("token", 6_000));

    [Fact(DisplayName = "Should_FinishEveryPatternWithinBudget_When_AValueIs30KbOfTokenText")]
    public void Should_FinishEveryPatternWithinBudget_When_AValueIs30KbOfTokenText()
    {
        (string Kind, string Value)[] inputs = [("unterminated JSON string", "\"" + TokenRun), ("bare", TokenRun)];
        var patterns = LogRedactionDefaultsProbe.RedactionRegexes;
        Assert.NotEmpty(patterns);

        var overBudget = new List<string>();
        foreach (var pattern in patterns)
        {
            var regex = LogRedactionDefaultsProbe.Compile(pattern);
            foreach (var (kind, value) in inputs)
            {
                var median = MedianOfFiveRunsMilliseconds(regex, value);
                if (median >= MedianBudgetMilliseconds)
                {
                    overBudget.Add($"{median:F1} ms over the {kind} value: {pattern}");
                }
            }
        }

        Assert.Empty(overBudget);
    }

    [Theory(DisplayName = "Should_RedactTheWholeJsonMember_When_TheKeyNamesACredential")]
    [InlineData("""{"newPassword":"a\"b"}""")]
    [InlineData("""{"Password":"x"}""")]
    [InlineData("""{"api-key":"x"}""")]
    public void Should_RedactTheWholeJsonMember_When_TheKeyNamesACredential(string json)
    {
        Assert.Equal("{[REDACTED]}", LogRedactionDefaultsProbe.Redact(json));
    }

    [Fact(DisplayName = "Should_LeaveAJsonMember_When_TheKeyNamesNoCredential")]
    public void Should_LeaveAJsonMember_When_TheKeyNamesNoCredential()
    {
        const string json = """{"name":"visible-marker"}""";

        Assert.Equal(json, LogRedactionDefaultsProbe.Redact(json));
    }

    [Theory(DisplayName = "Should_RedactTheConsoleCredential_When_TheQueryCarriesIt")]
    [InlineData("t=secret", "[REDACTED]")]
    [InlineData("a=1&t=secret", "a=1&[REDACTED]")]
    [InlineData("?t=secret", "?[REDACTED]")]
    [InlineData("?T=secret", "?[REDACTED]")]
    [InlineData("?%74=secret", "?[REDACTED]")]
    [InlineData("?%54=secret", "?[REDACTED]")]
    public void Should_RedactTheConsoleCredential_When_TheQueryCarriesIt(string query, string expected)
    {
        Assert.Equal(expected, LogRedactionDefaultsProbe.Redact(query));
    }

    [Theory(DisplayName = "Should_LeaveTheQuery_When_TheKeyOnlyEndsInT")]
    [InlineData("at=secret")]
    [InlineData("?at=secret")]
    [InlineData("?a=1&format=secret")]
    public void Should_LeaveTheQuery_When_TheKeyOnlyEndsInT(string query)
    {
        Assert.Equal(query, LogRedactionDefaultsProbe.Redact(query));
    }

    [Fact(DisplayName = "Should_RedactTheFormField_When_TheKeyNamesACredential")]
    public void Should_RedactTheFormField_When_TheKeyNamesACredential()
    {
        Assert.Equal("a=1&[REDACTED]", LogRedactionDefaultsProbe.Redact("a=1&password=x"));
    }

    [Theory(DisplayName = "Should_RemoveTheToken_When_APublicTokenPathIsInMixedCase")]
    [MemberData(nameof(PublicTokenPaths.MixedCaseTokenPaths), MemberType = typeof(PublicTokenPaths))]
    public void Should_RemoveTheToken_When_APublicTokenPathIsInMixedCase(string path, string nonSecretPrefix)
    {
        var redacted = LogRedactionDefaultsProbe.Redact(path);

        Assert.DoesNotContain(PublicTokenPaths.Token, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(nonSecretPrefix, redacted, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Should_LeaveThePath_When_ItIsNoPublicTokenRoute")]
    [InlineData("/api/v2/Public/ActionLinksX/abc123")]
    [InlineData("/api/v2/Public/Other/abc123")]
    [InlineData("/api/v2/Projects/abc123")]
    public void Should_LeaveThePath_When_ItIsNoPublicTokenRoute(string path)
    {
        Assert.Equal(path, LogRedactionDefaultsProbe.Redact(path));
    }

    [Fact(DisplayName = "Should_ListExactlyTheCredentialHeaders")]
    public void Should_ListExactlyTheCredentialHeaders()
    {
        Assert.Equal(
            LogRedactionDefaultsProbe.ExpectedSensitiveHeaders.Order(StringComparer.Ordinal),
            LogRedactionDefaultsProbe.SensitiveHeaders.Order(StringComparer.Ordinal));
    }

    private static double MedianOfFiveRunsMilliseconds(Regex regex, string value)
    {
        var runs = new double[5];
        for (var i = 0; i < runs.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = regex.Replace(value, LogRedactionDefaultsProbe.Redacted);
            runs[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Array.Sort(runs);
        return runs[runs.Length / 2];
    }
}