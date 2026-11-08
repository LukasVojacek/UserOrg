using System.Text.Json;

namespace Forge.Core.Features.ImportExport;

/// <summary>
/// Interface for features that support import and export functionality.
/// Implementations provide streaming-based export and JSON-based import capabilities.
/// </summary>
public interface IForgeImportExport
{
    /// <summary>
    /// Gets the unique name of the feature (e.g., "permissions", "roles").
    /// </summary>
    string FeatureName { get; }

    /// <summary>
    /// Exports the feature data by writing directly to the provided Utf8JsonWriter.
    /// This enables streaming without buffering entire datasets in memory.
    /// </summary>
    /// <param name="writer">The JSON writer to write the feature data to.</param>
    /// <param name="options">Options controlling what data to export.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A task representing the async export operation.</returns>
    Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Imports feature data from the provided JSON payload.
    /// </summary>
    /// <param name="payload">The JSON element containing the feature's data to import.</param>
    /// <param name="options">Options controlling import behavior (e.g., fail-fast, overwrite).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A task containing the import result with statistics and any errors.</returns>
    // Implement by deriving from TypedForgeImportExport<TPayload>, which seals this entry point
    // so no import can skip cache invalidation (ADR-0084). Implementing this interface directly
    // reopens that hole.
    Task<ImportResult> ImportAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken);
}