namespace UserOrg.ServiceDefaults.Tests.Logging;

internal static class PublicTokenPaths
{
    public const string Token = "abc123";

    public static readonly string[] ExcludePrefixes =
    [
        "/api/v2/Public/ActionLinks/*",
        "/api/v2/Public/Requests/ByLink/*",
        "/api/v2/Public/ResourceRequestActions/*",
        "/api/Public/Handle/*",
        "/api/Action/Handle/*"
    ];

    public static TheoryData<string> Prefixes => [.. ExcludePrefixes];

    private static readonly (string Path, string NonSecretPrefix)[] MixedCase =
    [
        ("/API/v2/public/ACTIONLINKS/abc123", "/API/v2"),
        ("/api/V2/PUBLIC/requests/BYLINK/abc123", "/api/V2"),
        ("/Api/v2/Public/resourcerequestactions/abc123", "/Api/v2"),
        ("/API/public/HANDLE/abc123", "/API"),
        ("/api/USERORG/api/action/HANDLE/abc123", "/api/USERORG/api")
    ];

    public static TheoryData<string, string> MixedCaseTokenPaths => [.. MixedCase];

    public static TheoryData<string> MixedCaseTokenPathsOnly => [.. MixedCase.Select(static entry => entry.Path)];

    public static string TokenPathUnder(string prefix) => MixCase(prefix.TrimEnd('*')) + Token;

    public static string SiblingOf(string prefix) => prefix.TrimEnd('*').TrimEnd('/') + "X";

    private static string MixCase(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = i % 2 == 0 ? char.ToUpperInvariant(source[i]) : char.ToLowerInvariant(source[i]);
            }
        });
}