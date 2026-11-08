namespace Core.DataAccess.ResourceRequests;

/// <summary>
/// Core-owned read-model / projection row for the resource-assignment request flow (ADR-0063). The
/// WorkflowEngine owns the process; this row serves list, detail, slug and token lookups (engine step
/// payloads are opaque JSON blobs). Flow step handlers update <see cref="State"/> / <see cref="TokenHash"/> /
/// <see cref="WorkflowRequestId"/> on each transition; the create endpoint inserts the row. Plain
/// infrastructure entity — no domain base type, no soft-delete — mapped into its own schema-isolated unit.
/// </summary>
public sealed class ProjectResourceRequest
{
    /// <summary>Public identifier and primary key: <c>{projectShortName}-{resourceName}</c> lower-kebab, unique (ADR-0066).</summary>
    public string Slug { get; set; } = null!;

    /// <summary>The requesting project (natural key snapshot).</summary>
    public string ProjectShortName { get; set; } = null!;

    /// <summary>The provider owning the requested resource (natural key snapshot).</summary>
    public string ProviderName { get; set; } = null!;

    /// <summary>The requested resource name (natural key snapshot).</summary>
    public string ResourceName { get; set; } = null!;

    /// <summary>The identity-provider id of the owner/manager who created the request.</summary>
    public string RequesterIdpId { get; set; } = null!;

    /// <summary>Snapshot of the PI contact email the approval link was sent to.</summary>
    public string ContactEmail { get; set; } = null!;

    /// <summary>Current projection lifecycle state (ADR-0063 D9).</summary>
    public ResourceRequestState State { get; set; }

    /// <summary>Hash of the current single-use PI action-link token; <c>null</c> once invalidated (ADR-0064).</summary>
    public string? TokenHash { get; set; }

    /// <summary>Expiry of the current token; <c>null</c> when no active token.</summary>
    public DateTimeOffset? TokenExpiresAt { get; set; }

    /// <summary>The WorkflowEngine request/instance id driving this request.</summary>
    public Guid WorkflowRequestId { get; set; }

    /// <summary>Free-text reason captured on reject/cancel; <c>null</c> otherwise.</summary>
    public string? Reason { get; set; }

    /// <summary>When the request row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the request row was last transitioned.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}