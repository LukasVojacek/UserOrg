using Lukdrasil.StepUpLogging;

using UserOrg.ServiceDefaults.Logging;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The production logging defaults ship in code: a host with no appsettings.json at all resolves them from
/// AddServiceDefaults alone, in the API and in the Gateway.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class ProductionLoggingDefaultsTests
{
    public static readonly string[] DefaultExcludePaths =
    [
        "/health", "/alive", "/metrics", "/api/scalar.js", "/api/scalar.aspnetcore.js"
    ];

    [Theory(DisplayName = "Should_ResolveTheProductionLoggingDefaults_When_NoAppSettingsFileExists")]
    [InlineData("UserOrg.Api")]
    [InlineData("UserOrg.Gateway")]
    public async Task Should_ResolveTheProductionLoggingDefaults_When_NoAppSettingsFileExists(string applicationName)
    {
        await using var host = LoggingPipelineHost.Build(applicationName: applicationName);

        var options = host.StepUpOptions;

        Assert.Equal(StepUpMode.Auto, options.Mode);
        Assert.Equal("Warning", options.BaseLevel);
        Assert.Equal("Information", options.StepUpLevel);
        Assert.Equal(60, options.DurationSeconds);
        Assert.Equal(600, options.MaxContinuousStepUpSeconds);
        Assert.Equal(300, options.StepUpCooldownSeconds);
        Assert.False(options.TreatServerErrorStatusAsError);
        Assert.True(options.AlwaysLogRequestSummary);
        Assert.False(options.CaptureRequestBody);
        Assert.True(options.EnablePreErrorBuffering);
        Assert.Equal(50, options.PreErrorBufferSize);
        Assert.Equal(256, options.PreErrorMaxContexts);
        Assert.Equal(DefaultExcludePaths, options.ExcludePaths);
    }

    [Fact(DisplayName = "Should_LetAMountedFileOverrideTheProductionLoggingDefaults")]
    public async Task Should_LetAMountedFileOverrideTheProductionLoggingDefaults()
    {
        await using var host = LoggingPipelineHost.Build([new("SerilogStepUp:DurationSeconds", "120")]);

        var options = host.StepUpOptions;

        Assert.Equal(120, options.DurationSeconds);
        Assert.Equal(600, options.MaxContinuousStepUpSeconds);
        Assert.Equal(DefaultExcludePaths, options.ExcludePaths);
    }

    [Fact(DisplayName = "Should_ExcludeTheFrameworkCategoriesFromThePreErrorBuffer_When_NoAppSettingsFileExists")]
    public async Task Should_ExcludeTheFrameworkCategoriesFromThePreErrorBuffer_When_NoAppSettingsFileExists()
    {
        await using var host = LoggingPipelineHost.Build();

        string[] expected =
        [
            .. FrameworkLevelOverrides.FrameworkCategories,
            "Microsoft.EntityFrameworkCore.Database.Command",
            "Microsoft.EntityFrameworkCore.ChangeTracking",
            "Microsoft.EntityFrameworkCore.Query",
            "Microsoft.EntityFrameworkCore.Update",
            "Microsoft.AspNetCore.Hosting.Diagnostics",
        ];
        Assert.Equal(expected, host.StepUpOptions.PreErrorBufferExcludeCategories);
    }
}
