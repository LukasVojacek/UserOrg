using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.AppHost.Tests;

/// <summary>
/// Loads and parses the AppHost's own source files.
/// </summary>
/// <remarks>
/// <para>
/// The AppHost is an executable whose entire composition lives in the top-level statements of
/// <c>Program.cs</c>, ending in <c>await builder.Build().RunAsync()</c>. There is no seam that hands
/// the resource model to a caller, and ADR-0069 retired the
/// <c>DistributedApplicationTestingBuilder</c> boot path from this repository's test suite — so the
/// model itself is out of reach here. What is in reach, and what these tests assert, is the shape of
/// the composition: which resources are registered, whether a registration sits behind a build-time
/// <c>if</c>, and whether a value is produced at model-build time or inside a resource-start callback.
/// </para>
/// <para>
/// That distinction is exactly the defect this block fixes, so the syntax is a faithful proxy for it —
/// but it is a proxy. Whether the dashboard dialog actually drives the topology can only be shown by
/// running <c>aspire run</c>.
/// </para>
/// </remarks>
static class AppHostSource
{
    /// <summary>The repository root, located by the solution file next to it.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary><c>src/UserOrg.AppHost</c>.</summary>
    public static string Directory { get; } = Path.Combine(RepositoryRoot, "src", "UserOrg.AppHost");

    /// <summary>The raw text of <c>Program.cs</c>, byte for byte.</summary>
    public static string ProgramText => TextOf("Program.cs");

    /// <summary>The parsed root of <c>Program.cs</c>.</summary>
    public static CompilationUnitSyntax Program => RootOf("Program.cs");

    /// <summary>The parsed root of the file that declares <c>AddParameters</c>.</summary>
    public static CompilationUnitSyntax ParameterExtensions => RootOf("IDistributedApplicationBuilderExtensions.cs");

    /// <summary>The parsed root of the file that declares the migration executables.</summary>
    public static CompilationUnitSyntax MigrationToolExtensions => RootOf("MigrationToolExtssensions.cs");

    /// <summary>Every C# file under <c>src/UserOrg.AppHost</c>, by repo-relative path.</summary>
    public static IReadOnlyDictionary<string, string> AllFiles { get; } = LoadAllFiles();

    public static string TextOf(string fileName)
    {
        var path = Path.Combine(Directory, fileName);
        Assert.True(File.Exists(path), $"expected AppHost source file '{path}' to exist");
        return File.ReadAllText(path);
    }

    public static CompilationUnitSyntax RootOf(string fileName) => (CompilationUnitSyntax)Parse(TextOf(fileName)).GetRoot();

    /// <summary>
    /// Parses AppHost source. LanguageVersion.Preview is required: the AppHost uses C# 14 extension
    /// blocks (`extension(IDistributedApplicationBuilder builder)`), which the pinned
    /// Microsoft.CodeAnalysis.CSharp cannot read at its default language version.
    /// </summary>
    public static SyntaxTree Parse(string text) =>
        CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));

    private static Dictionary<string, string> LoadAllFiles() =>
        System.IO.Directory
            .EnumerateFiles(Directory, "*.cs", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(RepositoryRoot, path), File.ReadAllText);

    private static string FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
    {
        // The build output lands under <root>/artifacts (UseArtifactsOutput), so walking up from the
        // test binary finds the root; the compile-time path of this file is the fallback.
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(callerFilePath) })
        {
            for (var directory = start; !string.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
            {
                if (File.Exists(Path.Combine(directory, "UserOrganization.slnx")))
                {
                    return directory;
                }
            }
        }

        throw new InvalidOperationException(
            $"could not locate UserOrganization.slnx above '{AppContext.BaseDirectory}' or '{callerFilePath}'");
    }
}
