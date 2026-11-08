namespace UserOrg.Shared.Hashing.Tests;

/// <summary>
/// Drives <see cref="IdentityHashGenerator"/> directly against small source snippets to verify each
/// IDHASH00x diagnostic actually fires for the condition it claims to catch — none of these can be
/// verified by a normal build, since the bad snippet would just fail the whole test project's
/// compilation instead of surfacing as an assertable diagnostic.
/// </summary>
public sealed class IdentityHashGeneratorDiagnosticsTests
{
    [Fact]
    public void Should_ReportMissingPartial_When_LeafTypeIsNotPartial()
    {
        const string source = """
            using UserOrg.Shared.Hashing;
            namespace T;
            [GenerateIdentityHash]
            public sealed record Foo(string A);
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.MissingPartial.Id);
    }

    [Fact]
    public void Should_ReportGenericTypeNotSupported_When_LeafTypeHasTypeParameters()
    {
        const string source = """
            using UserOrg.Shared.Hashing;
            namespace T;
            [GenerateIdentityHash]
            public sealed partial record Foo<TValue>(TValue A);
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.GenericTypeNotSupported.Id);
    }

    [Fact]
    public void Should_ReportGenericTypeNotSupported_When_ContainingTypeHasTypeParameters()
    {
        const string source = """
            using UserOrg.Shared.Hashing;
            namespace T;
            public static partial class Outer<TValue>
            {
                [GenerateIdentityHash]
                public sealed partial record Foo(string A);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.GenericTypeNotSupported.Id);
    }

    [Fact]
    public void Should_ReportUnsupportedPropertyType_When_PropertyIsNeitherScalarNorIdentityHashed()
    {
        const string source = """
            using System.Text;
            using UserOrg.Shared.Hashing;
            namespace T;
            [GenerateIdentityHash]
            public sealed partial record Foo(StringBuilder A);
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.UnsupportedPropertyType.Id);
    }

    [Fact]
    public void Should_ReportUnsupportedContainingTypeKind_When_ContainerIsAnInterface()
    {
        const string source = """
            using UserOrg.Shared.Hashing;
            namespace T;
            public interface IOuter
            {
                [GenerateIdentityHash]
                sealed partial record Foo(string A);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.UnsupportedContainingTypeKind.Id);
    }

    [Fact]
    public void Should_ReportContainingTypeMustBePartial_When_ContainerIsNotPartial()
    {
        const string source = """
            using UserOrg.Shared.Hashing;
            namespace T;
            public static class Outer
            {
                [GenerateIdentityHash]
                public sealed partial record Foo(string A);
            }
            """;

        var (diagnostics, _) = GeneratorTestHarness.Run(source);

        Assert.Contains(diagnostics, d => d.Id == IdentityHashDiagnostics.ContainingTypeMustBePartial.Id);
    }

    [Fact]
    public void Should_ReportNoDiagnostics_When_TypeIsValid()
    {
        const string source = """
            using System.Collections.Generic;
            using UserOrg.Shared.Hashing;
            namespace T;
            public static partial class Outer
            {
                [GenerateIdentityHash]
                public sealed partial record Foo(string A, IReadOnlyList<Bar> Items);

                [GenerateIdentityHash]
                public sealed partial record Bar(string B);
            }
            """;

        var (diagnostics, outputCompilation) = GeneratorTestHarness.Run(source);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(
            outputCompilation.GetDiagnostics(),
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
