using System.Runtime.CompilerServices;

namespace Core.UnitTests.Shared;

public sealed class RawNameCommentGuardTests
{
    [Theory]
    [InlineData("Authorize the raw provider name")]
    [InlineData("Authorize the raw name before parsing it")]
    public void Should_NotRestateProviderWriteAuthorizationDoc_When_ScanningCoreModule(string phrase)
    {
        var coreDirectory = Path.Combine(FindRepositoryRoot(), "src", "Modules", "Core");

        var offenders = Directory
            .EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(phrase, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(coreDirectory, path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string FindRepositoryRoot([CallerFilePath] string callerFilePath = "")
    {
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