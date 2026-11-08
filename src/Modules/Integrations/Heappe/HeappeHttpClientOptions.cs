using System.ComponentModel.DataAnnotations;

namespace Heappe;

/// <summary>
/// Configurable HTTP timeouts for the HEAppE integration clients.
/// <see cref="AttemptTimeoutSeconds"/>/<see cref="TotalRequestTimeoutSeconds"/> drive the standard resilience
/// pipeline of the data client (<see cref="HeappeIntegrationExtensions.HeappeClientName"/>);
/// <see cref="HealthProbeTimeoutSeconds"/> is the flat timeout of the separate version/health probe client
/// (<see cref="HeappeIntegrationExtensions.HeappeHealthClientName"/>).
/// </summary>
/// <remarks>
/// These values intentionally override the global standard resilience handler registered in ServiceDefaults
/// (which leaves <c>AttemptTimeout</c> at its 10 s default). The HEAppE clients remove that inherited handler
/// and configure their own — see <see cref="HeappeIntegrationExtensions"/>.
/// </remarks>
internal sealed class HeappeHttpClientOptions : IValidatableObject
{
    internal static string SectionName = "Heappe:HttpClient";

    /// <summary>
    /// Per-attempt timeout (seconds): how long a single HTTP attempt may take before being canceled and retried.
    /// HEAppE endpoints (e.g. ListAvailableClusters) can legitimately take longer under load, hence the high default.
    /// </summary>
    [Range(1, 600)]
    public int AttemptTimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// Total request timeout (seconds): the overall budget across all attempts/retries. Must be >= <see cref="AttemptTimeoutSeconds"/>.
    /// </summary>
    [Range(1, 1800)]
    public int TotalRequestTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Flat timeout (seconds) for the version/health probe, served by a dedicated client with no retries and no
    /// shared circuit breaker. Kept short so a slow/unreachable HEAppE instance falls back to the DB version
    /// resolver quickly instead of blocking the TemplatesInfo build.
    /// </summary>
    [Range(1, 120)]
    public int HealthProbeTimeoutSeconds { get; set; } = 15;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TotalRequestTimeoutSeconds < AttemptTimeoutSeconds)
        {
            yield return new ValidationResult(
                $"{nameof(TotalRequestTimeoutSeconds)} ({TotalRequestTimeoutSeconds}s) must be >= {nameof(AttemptTimeoutSeconds)} ({AttemptTimeoutSeconds}s).",
                [nameof(TotalRequestTimeoutSeconds), nameof(AttemptTimeoutSeconds)]);
        }
    }
}