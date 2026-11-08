using Microsoft.Extensions.Configuration;

using UserOrg.ServiceDefaults.Logging;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// A framework category is held by a StepUpLogging category floor that Diagnostic mode lifts (ADR 0112); a Serilog
/// root override on it in a committed example file would drop its events before the floor in every mode, so the
/// example files carry a root override only for an exporter or lifecycle category.
/// </summary>
public sealed class CommittedAppSettingsOverrideTests
{
    private const string OverridePrefix = "Serilog:MinimumLevel:Override:";

    private static readonly string[] ExampleFiles =
    [
        Path.Combine("src", "Core", "API", "appsettings.example.json"),
        Path.Combine("src", "Modules", "Services", "Gateway", "UserOrg.Gateway", "appsettings.example.json"),
    ];

    [Fact(DisplayName = "Should_CarryNoRootOverrideOnAFrameworkCategory_In_TheCommittedExampleFiles")]
    public void Should_CarryNoRootOverrideOnAFrameworkCategory_In_TheCommittedExampleFiles()
    {
        var root = RepositoryRoot();
        var files = ExampleFiles.Where(file => File.Exists(Path.Combine(root, file))).ToList();
        Assert.Contains(ExampleFiles[1], files);

        var offences = files
            .SelectMany(file => new ConfigurationBuilder().AddJsonFile(Path.Combine(root, file)).Build().AsEnumerable()
                .Select(entry => entry.Key)
                .Where(IsFrameworkRootOverride)
                .Select(key => $"{file}: {key}"))
            .ToList();

        Assert.True(offences.Count == 0, $"root overrides on framework categories: {string.Join("; ", offences)}");
    }

    [Theory(DisplayName = "Should_FlagARootOverride_When_ItEqualsOrExtendsAFrameworkCategory")]
    [InlineData("Serilog:MinimumLevel:Override:System", true)]
    [InlineData("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore", true)]
    [InlineData("Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Mvc", true)]
    [InlineData("Serilog:MinimumLevel:Override:Polly.Retry", true)]
    [InlineData("Serilog:MinimumLevel:Override:Microsoft.EntityFrameworkCore.Database.Command", true)]
    [InlineData("Serilog:MinimumLevel:Override:Microsoft.AspNetCore.Hosting.Diagnostics", true)]
    [InlineData("Serilog:MinimumLevel:Override:System.Net.Http.HttpClient.OtlpLogExporter", false)]
    [InlineData("Serilog:MinimumLevel:Override:Microsoft.Hosting.Lifetime", false)]
    [InlineData("Serilog:MinimumLevel:Override:UserOrg.Core", false)]
    [InlineData("Serilog:MinimumLevel:Default", false)]
    public void Should_FlagARootOverride_When_ItEqualsOrExtendsAFrameworkCategory(string key, bool expected) =>
        Assert.Equal(expected, IsFrameworkRootOverride(key));

    private static bool IsFrameworkRootOverride(string key)
    {
        if (!key.StartsWith(OverridePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var category = key[OverridePrefix.Length..];
        string[] allowed = [.. FrameworkLevelOverrides.ExporterCategories, FrameworkLevelOverrides.LifecycleCategory];

        return !allowed.Contains(category, StringComparer.Ordinal)
            && FrameworkLevelOverrides.FrameworkCategories.Any(framework =>
                category == framework || category.StartsWith(framework + ".", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UserOrganization.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"no UserOrganization.slnx above {AppContext.BaseDirectory}");
    }
}