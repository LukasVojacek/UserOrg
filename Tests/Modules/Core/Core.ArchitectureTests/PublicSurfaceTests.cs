using System.Reflection;
using System.Runtime.CompilerServices;

using Core.Composition;

namespace Core.ArchitectureTests;

/// <summary>
/// Freezes the public surface of the <c>Core</c> module. Everything in <c>src/Modules/Core</c>
/// is <c>internal</c> by default (ADR-0079); the only exported types are the composition seam
/// the host calls plus two documented exceptions. A new public leak fails this test.
/// Hand-rolled reflection in the style of <see cref="CoreArchitectureTests"/> — no NetArchTest.
/// </summary>
public sealed class PublicSurfaceTests
{
    // Anchored to a public composition type so the Core use-case assembly is loaded.
    private static readonly Assembly CoreAssembly = typeof(ApiAudience).Assembly;

    // The frozen allowlist (ADR-0079): composition seam plus two documented exceptions. The port
    // IResourceSyncRunner is implemented by Userorg.Adapter/Heappe, and UseCoreConditionalGet on
    // ConditionalGetApplicationBuilderExtensions is called by the host.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "Core.Composition.ServiceCollectionExtensions",
        "Core.Composition.EndpointsExtensions",
        "Core.Composition.ApiAudience",
        "Core.Shared.Http.ConditionalGetApplicationBuilderExtensions",
        "Core.Features.Provider.ResourceAssignment.IResourceSyncRunner",
    };

    [Fact]
    public void Should_ExportExactlyTheAllowlist_When_InspectingCorePublicSurface()
    {
        var exported = CoreAssembly.GetExportedTypes()
            .Where(t => !IsCompilerGenerated(t))
            .Select(t => t.FullName ?? t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var leaked = exported.Except(Allowlist).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var missing = Allowlist.Except(exported).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.True(
            leaked.Length == 0 && missing.Length == 0,
            $"Core public surface must equal the ADR-0079 allowlist exactly.{(leaked.Length > 0 ? $"{Environment.NewLine}  + leaked (should be internal): {string.Join(", ", leaked)}" : string.Empty)}{(missing.Length > 0 ? $"{Environment.NewLine}  - missing (expected public): {string.Join(", ", missing)}" : string.Empty)}");
    }

    // C# 14 extension members emit compiler-generated nested container types (`<G>$...`) under the
    // public composition classes. They are an implementation detail of the language, not surface.
    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || (type.FullName ?? type.Name).Contains('<', StringComparison.Ordinal);
}
