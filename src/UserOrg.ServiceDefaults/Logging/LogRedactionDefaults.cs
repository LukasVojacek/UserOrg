using System.Text.RegularExpressions;

using Lukdrasil.StepUpLogging;

namespace UserOrg.ServiceDefaults.Logging;

/// <summary>The redaction configuration both hosts share, assigned in code so configuration cannot weaken it (ADR 0112).</summary>
public static partial class LogRedactionDefaults
{
    private const string CredentialKey =
        "(?:passw(?:or)?d|pwd|secret|token|api[_-]?key|private[_-]?key|credential|authorization|action_?link)";

    private const string PublicTokenPathPattern =
        """(?i)/(?:Public/(?:ActionLinks|Requests/ByLink|ResourceRequestActions|Handle)|Action/Handle)/[^/?\s"]+""";

    public static IReadOnlyList<string> RedactionRegexes { get; } =
    [
        $$"""
            (?i)"[\w.\-]{0,64}?{{CredentialKey}}[\w.\-]{0,64}"\s*:\s*"(?:[^"\\]|\\.)*"
            """,
        $"(?i){CredentialKey}=[^&]*",
        "(?i)(?<![^?&])(?:t|%[57]4)=[^&]*",
        PublicTokenPathPattern,
    ];

    public static IReadOnlyList<string> SensitiveHeaders { get; } =
        ["x-original-authorization", "x-api-servicetoken", "x-api-providerservicekey", "x-api-token", "x-api-key", "authorization", "x-session-code", "cookie"];

    public static void Apply(StepUpLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RedactLogEventProperties = true;
        options.RedactionRegexes = [.. RedactionRegexes];
        options.AdditionalSensitiveHeaders = [.. SensitiveHeaders];
    }

    [GeneratedRegex(PublicTokenPathPattern, RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex PublicTokenPath();
}