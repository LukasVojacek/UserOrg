using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

static class VaultSyntax
{
    private static readonly string[] Markers =
    [
        "\"Vault\"",
        "\"VaultProxy\"",
        "\"vault-setup\"",
        "role_id",
        "X-Vault-Token",
        "userorg_pol"
    ];

    public static string VaultVolumeDirectory { get; } =
        Path.Combine(AppHostSource.Directory, "externalServices", "vaultVolume");

    public static IReadOnlyDictionary<string, string> Files { get; } = LoadVaultFiles();

    public static string Code { get; } = string.Join('\n', Files.Values);

    public static string AppHostCode { get; } =
        string.Join('\n', ResourceCommandSyntax.AllRoots.Values.Select(root => root.Code()));

    public static IReadOnlyList<string> Literals { get; } =
        [.. ResourceCommandSyntax.AllRoots
            .Where(file => Files.ContainsKey(file.Key))
            .SelectMany(file => file.Value.DescendantNodes()
                .OfType<LiteralExpressionSyntax>()
                .Where(literal => literal.IsKind(SyntaxKind.StringLiteralExpression))
                .Select(literal => literal.Token.ValueText))];

    public static IReadOnlyList<InvocationExpressionSyntax> Calls(string method, string? withStringArgument = null) =>
        [.. ResourceCommandSyntax.AllRoots.Values.SelectMany(root => root.Calls(method, withStringArgument))];

    public static InvocationExpressionSyntax Call(string method, string? withStringArgument = null)
    {
        var matches = Calls(method, withStringArgument);
        var description = withStringArgument is null ? method : $"{method}(\"{withStringArgument}\", …)";

        Assert.True(
            matches.Count == 1,
            $"expected exactly one `{description}` call in the AppHost, found {matches.Count}");

        return matches[0];
    }

    public static IReadOnlyList<InvocationExpressionSyntax> AppliedCalls(
        this InvocationExpressionSyntax anchor,
        string method)
    {
        var root = anchor.SyntaxTree.GetRoot();
        var variable = anchor.AssignedVariable();

        IEnumerable<InvocationExpressionSyntax> onTheVariable = variable is null
            ? []
            : root.Calls(method).Where(call => call.Receiver() == variable);

        return [.. anchor.Chain().Calls(method).Concat(onTheVariable).Distinct()];
    }

    public static bool AppliesTo(this InvocationExpressionSyntax anchor, string method) =>
        anchor.AppliedCalls(method).Count > 0;

    public static string VaultVolumeFile(string name)
    {
        var path = Path.Combine(VaultVolumeDirectory, name);
        Assert.True(File.Exists(path), $"expected `{path}` to exist");

        return File.ReadAllText(path);
    }

    public static bool VaultVolumeFileExists(string name) =>
        File.Exists(Path.Combine(VaultVolumeDirectory, name));

    public static string? AgentConfigValue(string key)
    {
        var match = Regex.Match(VaultVolumeFile("vault-agent.hcl"), key + @"\s*=\s*""([^""]+)""");

        return match.Success ? match.Groups[1].Value : null;
    }

    public static string Compact(this string code) =>
        Regex.Replace(code.Replace("\\", string.Empty, StringComparison.Ordinal), @"\s+", string.Empty);

    private static Dictionary<string, string> LoadVaultFiles()
    {
        var files = ResourceCommandSyntax.AllRoots
            .Select(file => (file.Key, Code: file.Value.Code()))
            .Where(file => Markers.Any(marker => file.Code.Contains(marker, StringComparison.Ordinal)))
            .ToDictionary(file => file.Key, file => file.Code);

        Assert.True(
            files.Count > 0,
            "no AppHost source file mentions the Vault resources; the Vault composition tests would go vacuously green");

        return files;
    }
}
