using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace UserOrg.Shared.Hashing.Tests;

/// <summary>Runs <see cref="IdentityHashGenerator"/> against a source snippet, outside of a real build.</summary>
internal static class GeneratorTestHarness
{
    // Reusing every assembly already loaded into this test process (BCL facades + UserOrg.Shared.Hashing
    // itself, via this project's ProjectReference) sidesteps hand-curating a MetadataReference set.
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location)),
    ];

    public static (ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation OutputCompilation) Run(string source)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "GeneratorTestHarness.Compilation",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: References,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new IdentityHashGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (diagnostics, outputCompilation);
    }
}
