using System.Runtime.CompilerServices;

using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Audit;
using Heappe.Features.Connection;
using Heappe.Features.Nexus;
using Heappe.HostContracts;

using Lukdrasil.Result;

using Microsoft.Extensions.Options;

namespace Heappe.Features.ResourceValidityAudit;

/// <summary>
/// Default audit: lists targets through the host port, then for each verifies the HEAppE project record exists
/// and its validity matches userorg, streaming one <see cref="TargetAuditResult"/> per target. Read-only.
/// </summary>
internal sealed class HeappeResourceAuditService(
    IHeappeAuditTargetProvider targetProvider,
    IHeappeConnectionFactory connectionFactory,
    IHeappeService heappeService,
    IOptions<TokenExchangeOptions> options) : IHeappeResourceAuditService
{
    public async IAsyncEnumerable<TargetAuditResult> RunAuditAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var targets = await targetProvider.GetAuditTargetsAsync(ct);

        // Same resource reached via two locations on the same instance is one target (ADR-0025).
        var seen = new HashSet<(string Resource, string Instance)>();
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add((target.ResourceName, target.HeappeIdentifier.Value)))
            {
                continue;
            }

            yield return await EvaluateAsync(target, ct);
        }
    }

    private async ValueTask<TargetAuditResult> EvaluateAsync(HeappeAuditTargetEntry target, CancellationToken ct)
    {
        var expectedStart = ToUtcDate(target.StartDate);
        var expectedEnd = ToUtcDate(target.EndDate);

        var connection = await connectionFactory.ForServiceAsync(target.HeappeIdentifier, ct);
        if (!connection.IsSuccess)
        {
            return CheckFailed(target, expectedStart, expectedEnd, $"Could not open a HEAppE service connection: {connection.Error.Description}");
        }

        var project = await heappeService.GetProjectByResourceName(connection.Value, target.ResourceName, ct);
        if (!project.IsSuccess)
        {
            return project.State == State.NotFound
                ? new TargetAuditResult(target.ResourceName, target.ProjectShortName, InstanceOf(target), AuditStatus.Missing, expectedStart, expectedEnd, null, null, null)
                : CheckFailed(target, expectedStart, expectedEnd, project.Error.Description);
        }

        // ponytail: HEAppE dates are taken at their date component as-is — validity is provisioned whole-day,
        // so a calendar-day compare against the UTC-normalised userorg date holds. Upgrade to kind-aware
        // normalisation only if a HEAppE instance ever returns a timezone-shifted instant.
        var actualStart = DateOnly.FromDateTime(project.Value.StartDate);
        var actualEnd = DateOnly.FromDateTime(project.Value.EndDate);

        var status = expectedStart == actualStart && expectedEnd == actualEnd
            ? AuditStatus.Ok
            : AuditStatus.ValidityMismatch;

        return new TargetAuditResult(target.ResourceName, target.ProjectShortName, InstanceOf(target), status, expectedStart, expectedEnd, actualStart, actualEnd, null);
    }

    // The audit card groups findings by instance; the direct URL is only a label, so fall back to the proxy address.
    private Uri InstanceOf(HeappeAuditTargetEntry target) =>
        target.HeappeUri ?? NexusEndpoints.ProxyBaseFor(options.Value.Nexus.BaseUrl!, target.HeappeIdentifier);

    private static DateOnly ToUtcDate(DateTimeOffset value) => DateOnly.FromDateTime(value.UtcDateTime);

    private TargetAuditResult CheckFailed(HeappeAuditTargetEntry target, DateOnly expectedStart, DateOnly expectedEnd, string reason) =>
        new(target.ResourceName, target.ProjectShortName, InstanceOf(target), AuditStatus.CheckFailed, expectedStart, expectedEnd, null, null, reason);
}