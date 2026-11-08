using System.Text.Json;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.ResourceRequest.Flow;

/// <summary>Step messages for the assign-resource-to-project flow. Each carries only the projection slug — handlers re-read needed data from the projection row, keeping payloads small and transitions pure.</summary>

/// <summary>Start step: mints the PI action link, stores its hash, and emails the PI the approval request.</summary>
internal sealed record SendPiApprovalEmailStep(Guid RequestId, string Slug)
    : StepRequest(RequestId, "SendPiApprovalEmail");

/// <summary>Looks the requested resource up in the catalog and routes: found → Attach, missing → NotifyProviderResourceNeeded.</summary>
internal sealed record CheckResourceExistsStep(Guid RequestId, string Slug)
    : StepRequest(RequestId, "CheckResourceExists");

/// <summary>Attaches the resource to the project via the F5 write path and records the projection Assigned.</summary>
internal sealed record AttachResourceStep(Guid RequestId, string Slug)
    : StepRequest(RequestId, "AttachResource");

/// <summary>Notifies the requester that the resource was assigned.</summary>
internal sealed record NotifyRequesterAssignedStep(Guid RequestId, string Slug)
    : StepRequest(RequestId, "NotifyRequesterAssigned");

/// <summary>Terminal reject step: records rejection and emails the requester the PI's reason.</summary>
internal sealed record NotifyRequesterRejectedStep(Guid RequestId, string Slug, string? Reason)
    : StepRequest(RequestId, "NotifyRequesterRejected");

/// <summary>Wait path: parks projection at AwaitingResourceCreation and emails the provider to create the resource.</summary>
internal sealed record NotifyProviderResourceNeededStep(Guid RequestId, string Slug)
    : StepRequest(RequestId, "NotifyProviderResourceNeeded");

/// <summary>
/// Terminal cancel step: clears action links (token hash + expiry) and records the projection <c>Cancelled</c>,
/// or <c>Expired</c> when <paramref name="Expired"/> is true (system-initiated by the expiry sweeper only,
/// DEC-7). The sweeper resumes the gate with a cancel signal carrying this flag so both cancel and expiry
/// converge on the same step (DER-2) rather than requiring a second termination path.
/// </summary>
internal sealed record InvalidateLinksStep(Guid RequestId, string Slug, string? Reason, bool Expired = false)
    : StepRequest(RequestId, "InvalidateLinks");

/// <summary>
/// Result of <see cref="CheckResourceExistsStep"/>: whether the requested <c>(provider, resource name)</c> is in
/// the catalog and can therefore be attached now. The catalog lookup happens in the handler (I/O) and its answer
/// is carried here so the branch transition stays pure.
/// </summary>
/// <remarks>
/// The property name is part of the workflow checkpoint contract: it is JSON-serialized into the engine's
/// persisted <c>StepResult</c> and read back by <see cref="HasResource"/> whenever a completed step's transition
/// is replayed after a crash. This property was renamed from <c>Exists</c> in this same change; any <c>StepResult</c>
/// persisted before this deployment carries <c>{"Exists":true}</c> and will be read as <c>false</c> during
/// crash-recovery replay, silently parking requests that should have attached. This is safe only because no
/// <c>CheckResourceExists</c> checkpoints were in-flight at deploy time. Future renames require a payload migration
/// or a read-time alias to avoid the same hazard.
/// </remarks>
internal sealed record CheckResourceExistsResult(bool IsAssignable)
{
    internal StepResult ToStepResult() => new(JsonSerializer.SerializeToDocument(this));

    internal static bool HasResource(StepResult result) =>
        result.Data is not null
        && result.Data.RootElement.TryGetProperty(nameof(IsAssignable), out var p)
        && p.GetBoolean();
}