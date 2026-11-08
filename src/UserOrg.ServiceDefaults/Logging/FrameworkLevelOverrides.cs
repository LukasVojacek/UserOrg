using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.Hosting;

namespace UserOrg.ServiceDefaults.Logging;

/// <summary>Holds the framework categories at a Warning category floor that Diagnostic mode lifts, and the exporter and lifecycle categories at Serilog root overrides that no mode lifts (ADR 0112).</summary>
public static class FrameworkLevelOverrides
{
    public const string LifecycleCategory = "Microsoft.Hosting.Lifetime";

    private const string FloorPrefix = "SerilogStepUp:CategoryFloors:";
    private const string OverridePrefix = "Serilog:MinimumLevel:Override:";

    internal static IReadOnlyList<string> FrameworkCategories { get; } =
    [
        "Polly",
        "System.Net.Http.HttpClient",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.StaticFiles",
        "Microsoft.AspNetCore.Server.Kestrel",
        "Microsoft.AspNetCore.Http.Result",
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.EntityFrameworkCore",
        "Yarp.ReverseProxy",
        "Serilog.AspNetCore.RequestLoggingMiddleware",
        "Duende",
        "AspNetCoreRateLimit",
        "Ogham",
        "Microsoft",
        "System",
    ];

    internal static IReadOnlyList<string> ExporterCategories { get; } =
    [
        "System.Net.Http.HttpClient.OtlpLogExporter",
        "System.Net.Http.HttpClient.OtlpMetricExporter",
        "System.Net.Http.HttpClient.OtlpTraceExporter",
    ];

    internal static IReadOnlyDictionary<string, string?> Values { get; } = BuildValues();

    /// <summary>Adds the values last, so appsettings and environment variables cannot override the same key; a more specific <c>SerilogStepUp:CategoryFloors:&lt;category&gt;</c> key from any configuration source still wins by prefix.</summary>
    public static IHostApplicationBuilder AddFrameworkLevelOverrides(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        ((IConfigurationBuilder)builder.Configuration).Add(new MemoryConfigurationSource { InitialData = Values });
        return builder;
    }

    private static Dictionary<string, string?> BuildValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in FrameworkCategories)
        {
            values[FloorPrefix + category] = "Warning";
        }

        foreach (var category in ExporterCategories)
        {
            values[OverridePrefix + category] = "Warning";
        }

        values[OverridePrefix + LifecycleCategory] = "Information";
        values["SerilogStepUp:AlwaysExportCategories:0"] = LifecycleCategory;
        return values;
    }
}