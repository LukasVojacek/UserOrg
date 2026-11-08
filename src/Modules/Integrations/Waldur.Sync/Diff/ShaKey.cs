namespace Waldur.Sync.Diff;

/// <summary>The entity-type discriminators used in <see cref="ShaKey.EntityType"/>.</summary>
public static class WaldurEntityType
{
    /// <summary>A provider (Waldur customer). Key: <c>(Provider, externalId)</c>.</summary>
    public const string Provider = "Provider";

    /// <summary>A project. Key: <c>(Project, externalId)</c>.</summary>
    public const string Project = "Project";

    /// <summary>A project membership. Key: <c>(Member, projectExternalId, userName)</c>.</summary>
    public const string Member = "Member";

    /// <summary>The per-instance storage-config slot. Key: <c>(StorageConfig, "config")</c>.</summary>
    public const string StorageConfig = "StorageConfig";
}

/// <summary>
/// Identity of one entry in an instance's SHA breadcrumb map (ADR-0055). The owning instance is
/// implicit (one map per instance worker), so the key is
/// <c>(entityType, externalId[, userName])</c>. <see cref="UserName"/> is populated only for
/// membership entries.
/// </summary>
/// <param name="EntityType">One of <see cref="WaldurEntityType"/>.</param>
/// <param name="ExternalId">The Waldur id (canonical "D" form) — or the storage-config slot name.</param>
/// <param name="UserName">Membership identity (ADR-0054/A2); null for non-membership keys.</param>
public readonly record struct ShaKey(string EntityType, string ExternalId, string? UserName = null)
{
    /// <summary>The single, fixed slot name for the per-instance storage-config hash (ADR-0061).</summary>
    public const string StorageConfigSlot = "config";

    /// <summary>Key for a provider (customer).</summary>
    public static ShaKey Provider(string externalId) => new(WaldurEntityType.Provider, externalId);

    /// <summary>Key for a project.</summary>
    public static ShaKey Project(string externalId) => new(WaldurEntityType.Project, externalId);

    /// <summary>Key for a <c>(project, user)</c> membership.</summary>
    public static ShaKey Member(string projectExternalId, string userName) => new(WaldurEntityType.Member, projectExternalId, userName);

    /// <summary>Key for the per-instance storage-config slot.</summary>
    public static ShaKey StorageConfig() => new(WaldurEntityType.StorageConfig, StorageConfigSlot);

    /// <summary>
    /// True when this key may be produced by a cold rebuild (<see cref="IShaMapRebuilder"/>). The
    /// storage-config slot is the one exception (ADR-0055/A4): it has no DB source of truth, so the
    /// worker fires one unconditional <c>StorageConfigurationChanged</c> after startup instead.
    /// </summary>
    public static bool IsColdRebuildable(ShaKey key) => key.EntityType != WaldurEntityType.StorageConfig;
}