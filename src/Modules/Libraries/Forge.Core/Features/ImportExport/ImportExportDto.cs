using System.Text.Json;

namespace Forge.Core.Features.ImportExport;

/// <summary>
/// Options controlling what data to export.
/// </summary>
/// <param name="Features">Optional list of specific features to export. If null, exports all registered features.</param>
/// <param name="Since">Optional timestamp to export only data modified since this date.</param>
public sealed record ExportOptions(
    IEnumerable<string>? Features = null,
    DateTimeOffset? Since = null);

/// <summary>
/// Options controlling import behavior.
/// </summary>
/// <param name="FailFast">If true, stops import immediately on first feature error. Default is false (continue on error).</param>
/// <param name="OverwriteExisting">If true, overwrites existing data. If false, may skip or merge depending on feature implementation.</param>
public sealed record ImportOptions(
    bool FailFast = false,
    bool OverwriteExisting = false);

/// <summary>
/// Result of importing a single feature's data.
/// </summary>
/// <param name="Success">Whether the import succeeded without errors.</param>
/// <param name="CreatedCount">Number of new items created during import.</param>
/// <param name="UpdatedCount">Number of existing items updated during import.</param>
/// <param name="IdMapping">Optional mapping from source IDs to newly created IDs (sourceId -> newId).</param>
/// <param name="Errors">Optional collection of error messages encountered during import.</param>
public sealed record ImportResult(
    bool Success,
    int CreatedCount = 0,
    int UpdatedCount = 0,
    IDictionary<string, string>? IdMapping = null,
    IEnumerable<string>? Errors = null);

/// <summary>
/// Top-level envelope structure for export JSON.
/// </summary>
/// <param name="FormatVersion">Version of the export format (e.g., "1.0").</param>
/// <param name="ExportedAt">Timestamp when the export was created.</param>
/// <param name="Source">Information about the source system.</param>
/// <param name="Features">Map of feature name to feature-specific data.</param>
/// <param name="Metadata">Optional additional metadata.</param>
public sealed record ExportEnvelope(
    string FormatVersion,
    DateTimeOffset ExportedAt,
    object Source,
    IDictionary<string, JsonElement> Features,
    object? Metadata = null);