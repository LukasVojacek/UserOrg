namespace UserOrg.AppHost.Tests;

public sealed class AppHostSensitiveHeadersTests
{
    private const string SensitiveHeadersOption = "AdditionalSensitiveHeaders";

    private static readonly string ProjectConfigurationFile = Path.Combine("src", "UserOrg.AppHost", "ProjectConfigurationExtensions.cs");

    [Fact]
    public void Should_ReadEveryAppHostSourceFile()
    {
        Assert.NotEmpty(AppHostSource.AllFiles);

        Assert.True(
            AppHostSource.AllFiles.TryGetValue(ProjectConfigurationFile, out var text),
            $"AppHostSource.AllFiles does not hold {ProjectConfigurationFile}; it holds [{string.Join(", ", AppHostSource.AllFiles.Keys)}]");

        Assert.Contains("WithSerilogEnvironment", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Should_LeaveTheSensitiveHeaderListToTheServiceDefaults()
    {
        var setters = AppHostSource.AllFiles
            .SelectMany(file => file.Value
                .Split('\n')
                .Select((line, index) => (file.Key, Line: index + 1, Text: line.Trim()))
                .Where(line => line.Text.Contains(SensitiveHeadersOption, StringComparison.Ordinal)))
            .Select(line => $"{line.Key}:{line.Line} {line.Text}")
            .ToList();

        Assert.True(
            setters.Count == 0,
            $"the AppHost still names {SensitiveHeadersOption}, so it overrides the header list the service defaults assign wholesale: {string.Join("; ", setters)}");
    }
}