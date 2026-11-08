using System.Text.Json;
using System.Text.RegularExpressions;

namespace UserOrganizationManagement.Application.Tests;

public sealed partial class V2RoutePascalCaseGuardTests
{
    private const string V2Prefix = "/api/v2/";

    public static TheoryData<string> V2DocumentFileNames =>
    [
        "API_v2-user.json",
        "API_v2-admin.json",
        "API_v2-provideradmin.json"
    ];

    [Theory]
    [MemberData(nameof(V2DocumentFileNames))]
    public void Should_UsePascalCaseLiteralSegments_When_ReadingV2Document(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile(fileName)));

        var v2Paths = doc.RootElement.GetProperty("paths").EnumerateObject()
            .Select(path => path.Name)
            .Where(path => path.StartsWith(V2Prefix, StringComparison.Ordinal))
            .ToArray();

        var violatingSegments = v2Paths
            .SelectMany(path => path[V2Prefix.Length..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => !segment.StartsWith('{') && !PascalCaseSegment().IsMatch(segment))
                .Select(segment => $"{path}: {segment}"))
            .ToArray();

        Assert.NotEmpty(v2Paths);
        Assert.True(
            violatingSegments.Length == 0,
            $"""
             {fileName} has v2 literal path segments that are not PascalCase:
             {string.Join(Environment.NewLine, violatingSegments)}
             """);
    }

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex PascalCaseSegment();
}