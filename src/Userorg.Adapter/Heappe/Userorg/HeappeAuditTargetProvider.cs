using Core.DataAccess.Queries.Contracts;

using Heappe.Contracts.Entries.Audit;
using Heappe.HostContracts;

using Microsoft.Extensions.Logging;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

namespace Userorg.Adapter.Heappe.Userorg;

/// <summary>
/// Host-side implementation of <see cref="IHeappeAuditTargetProvider"/> (ADR-0025). Reads the audit targets
/// from the modular Core read side (<see cref="IHeappeAuditTargetQuery"/>) — deliberately separate from the
/// legacy-backed <see cref="UserorgService"/> — and maps the read rows to <see cref="HeappeAuditTargetEntry"/>,
/// parsing the stored HEAppE identifier (the audit routes by it) and normalizing the stored HEAppE URL, kept for logs
/// only, to an absolute <see cref="Uri"/> or null.
/// </summary>
/// <param name="auditTargetQuery">The modular read query listing the audit targets. Cannot be null.</param>
/// <param name="logger">Logs targets dropped because of an unparseable stored HEAppE identifier.</param>
internal sealed class HeappeAuditTargetProvider(
    IHeappeAuditTargetQuery auditTargetQuery,
    ILogger<HeappeAuditTargetProvider> logger)
    : IHeappeAuditTargetProvider
{
    public async Task<IList<HeappeAuditTargetEntry>> GetAuditTargetsAsync(CancellationToken ct)
    {
        var rows = await auditTargetQuery.GetAuditTargetsAsync(ct);

        var entries = new List<HeappeAuditTargetEntry>(rows.Count);
        foreach (var row in rows)
        {
            // A single malformed stored HEAPPE_IDENTIFIER must not abort the whole audit — skip the row and warn so
            // the misconfiguration is visible, while every routable target is still audited.
            if (!HeappeIdentifier.TryParse(row.HeappeIdentifier, out var identifier))
            {
                logger.LogWarning(
                    "Skipping HEAppE audit target for resource '{ResourceName}': stored HEAppE identifier '{HeappeIdentifier}' is not valid.",
                    row.ResourceName, row.HeappeIdentifier);
                continue;
            }

            entries.Add(new HeappeAuditTargetEntry(
                row.ResourceName,
                row.ProjectShortName,
                LogUriOf(row.HeappeUrl),
                identifier,
                row.StartDate,
                row.EndDate));
        }

        return entries;
    }

    // The stored URL only labels the findings; a missing or malformed one leaves the label to the identifier.
    private static Uri? LogUriOf(string? heappeUrl)
        => Uri.TryCreate(heappeUrl, UriKind.Absolute, out var heappeUri) ? heappeUri.ToNormalizeUrl() : null;
}