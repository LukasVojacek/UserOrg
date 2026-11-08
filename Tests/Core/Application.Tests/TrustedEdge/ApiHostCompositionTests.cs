using System.Reflection;
using System.Text.RegularExpressions;

namespace UserOrganizationManagement.Application.Tests.TrustedEdge;

public sealed class ApiHostCompositionTests
{
    [Fact]
    public void Should_CarryAnInformationalVersion_When_TheApiAssemblyIsBuilt()
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        Assert.True(version is not null, "the API assembly carries no AssemblyInformationalVersionAttribute, so audit records fall back to version=unknown");
        Assert.False(string.IsNullOrWhiteSpace(version.InformationalVersion));
    }

    [Fact]
    public void Should_ApplyTrustedProxyForwardedHeadersFirst_When_TheApiPipelineIsBuilt()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Core", "API", "Program.cs"));
        var build = program.IndexOf("builder.Build()", StringComparison.Ordinal);
        Assert.True(build >= 0, "src/Core/API/Program.cs no longer calls builder.Build()");

        var firstUse = Regex.Match(program[build..], @"\bapp\.(Use\w*)\s*\(");

        Assert.True(firstUse.Success, "src/Core/API/Program.cs registers no middleware after builder.Build()");
        Assert.Equal("UseTrustedProxyForwardedHeaders", firstUse.Groups[1].Value);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UserOrganization.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "UserOrganization.slnx not found above the test output directory");
        return directory.FullName;
    }
}