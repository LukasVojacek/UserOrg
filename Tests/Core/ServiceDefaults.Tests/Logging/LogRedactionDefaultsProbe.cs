using System.Text.RegularExpressions;

using UserOrg.ServiceDefaults.Logging;

namespace UserOrg.ServiceDefaults.Tests.Logging;

internal static class LogRedactionDefaultsProbe
{
    public const string Redacted = "[REDACTED]";

    public static readonly string[] ExpectedSensitiveHeaders =
        ["x-original-authorization", "x-api-servicetoken", "x-api-providerservicekey", "x-api-token", "x-api-key", "authorization", "x-session-code", "cookie"];

    public static IReadOnlyList<string> RedactionRegexes => LogRedactionDefaults.RedactionRegexes;

    public static IReadOnlyList<string> SensitiveHeaders => LogRedactionDefaults.SensitiveHeaders;

    public static Regex Compile(string pattern) => new(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

    public static string Redact(string input) =>
        RedactionRegexes.Aggregate(input, static (text, pattern) => Compile(pattern).Replace(text, Redacted));
}