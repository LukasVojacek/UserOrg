namespace UserOrg.Gateway.Features.RoleManagement;

/// <summary>
/// Summary of import/export operation results.
/// </summary>
/// <param name="Success">Whether the operation succeeded without errors.</param>
/// <param name="CreatedCount">Number of new items created during import.</param>
/// <param name="UpdatedCount">Number of existing items updated during import.</param>
/// <param name="ErrorCount">Number of errors encountered.</param>
/// <param name="Errors">Optional collection of error messages.</param>
internal sealed record ImportExportSummary(
    bool Success,
    int CreatedCount = 0,
    int UpdatedCount = 0,
    int ErrorCount = 0,
    IReadOnlyCollection<string>? Errors = null);