using Microsoft.CodeAnalysis;

namespace UserOrg.AppHost.Tests;

static class RepositorySource
{
    private static readonly HashSet<string> SkippedDirectories = new(["bin", "obj", "artifacts"], StringComparer.OrdinalIgnoreCase);

    public static string SourceDirectory { get; } = Path.Combine(AppHostSource.RepositoryRoot, "src");

    public static IReadOnlyDictionary<string, SyntaxTree> AllTrees { get; } = LoadAllTrees();

    private static Dictionary<string, SyntaxTree> LoadAllTrees() =>
        Directory
            .EnumerateFiles(SourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(AppHostSource.RepositoryRoot, path))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar).Any(SkippedDirectories.Contains))
            .ToDictionary(
                relative => relative,
                relative => AppHostSource.Parse(File.ReadAllText(Path.Combine(AppHostSource.RepositoryRoot, relative))));
}