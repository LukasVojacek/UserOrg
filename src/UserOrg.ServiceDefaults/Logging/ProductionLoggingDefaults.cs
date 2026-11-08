using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace UserOrg.ServiceDefaults.Logging;

/// <summary>The production logging defaults, shipped as the lowest-priority configuration source.</summary>
public static class ProductionLoggingDefaults
{
    private const string PreErrorBufferExcludePrefix = "SerilogStepUp:PreErrorBufferExcludeCategories:";

    private static readonly string[] PreErrorBufferExcludeCategories =
    [
        .. FrameworkLevelOverrides.FrameworkCategories,
        // why: the explicit list of EF and Hosting.Diagnostics categories the person decided on, kept even where a framework prefix already covers them
        "Microsoft.EntityFrameworkCore.Database.Command",
        "Microsoft.EntityFrameworkCore.ChangeTracking",
        "Microsoft.EntityFrameworkCore.Query",
        "Microsoft.EntityFrameworkCore.Update",
        "Microsoft.AspNetCore.Hosting.Diagnostics",
    ];

    /// <summary>The default configuration keys; <c>SerilogStepUp:PreErrorBufferExcludeCategories</c> holds the framework categories, which an Error flush must not export below Warning.</summary>
    public static IReadOnlyDictionary<string, string?> Values { get; } = BuildValues();

    /// <summary>Inserts <see cref="Values"/> first, so a mounted appsettings.json and environment variables override them, once per builder. Inserting rebuilds every provider already added, so call it before a source that reaches out (Vault), and before AddStepUpLogging.</summary>
    public static IHostApplicationBuilder AddProductionLoggingDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var sources = ((IConfigurationBuilder)builder.Configuration).Sources;
        if (!sources.OfType<MemoryConfigurationSource>().Any(source => ReferenceEquals(source.InitialData, Values)))
        {
            sources.Insert(0, new MemoryConfigurationSource { InitialData = Values });
        }

        return builder;
    }

    private static Dictionary<string, string?> BuildValues()
    {
        var values = new Dictionary<string, string?>
        {
            ["SerilogStepUp:BaseLevel"] = "Warning",
            ["SerilogStepUp:StepUpLevel"] = "Information",
            ["SerilogStepUp:DurationSeconds"] = "60",
            ["SerilogStepUp:MaxContinuousStepUpSeconds"] = "600",
            ["SerilogStepUp:StepUpCooldownSeconds"] = "300",
            ["SerilogStepUp:TreatServerErrorStatusAsError"] = "false",
            ["SerilogStepUp:AlwaysLogRequestSummary"] = "true",
            ["SerilogStepUp:CaptureRequestBody"] = "false",
            ["SerilogStepUp:EnablePreErrorBuffering"] = "true",
            ["SerilogStepUp:PreErrorBufferSize"] = "50",
            ["SerilogStepUp:PreErrorMaxContexts"] = "256",
            ["SerilogStepUp:ExcludePaths:0"] = "/health",
            ["SerilogStepUp:ExcludePaths:1"] = "/alive",
            ["SerilogStepUp:ExcludePaths:2"] = "/metrics",
            ["SerilogStepUp:ExcludePaths:3"] = "/api/scalar.js",
            ["SerilogStepUp:ExcludePaths:4"] = "/api/scalar.aspnetcore.js",
        };

        for (var index = 0; index < PreErrorBufferExcludeCategories.Length; index++)
        {
            values[PreErrorBufferExcludePrefix + index] = PreErrorBufferExcludeCategories[index];
        }

        return values;
    }
}