namespace Waldur.Sync.Mapping;

/// <summary>
/// Constants the pure mapper needs, ported from the legacy <c>WaldurConstants</c>,
/// <c>WaldurEfpConstants</c>, <c>RoleInfoConstants</c>, <c>ApplicationConstants</c> and
/// <c>DbConstants</c>. Duplicated here (rather than referencing the EF-laden <c>UserOrg.Shared</c>)
/// to keep <c>Waldur.Sync</c> self-contained (references only <c>Waldur.Contracts</c>). The values
/// are load-bearing for round-trip hash equality (ADR-0055) — if a DB limit changes, update both.
/// </summary>
internal static class MappingConstants
{
    // ── DB field limits (mirror UserOrg.Shared.Persistance.DbConstants) — A2.2 truncation ──
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 150;
    public const int ExtendedDescriptionMaxLength = 500;

    // ── Waldur states / types (WaldurConstants) ──
    public const string WaldurStateArchived = "archived";
    public const string WaldurStateTerminated = "terminated";
    public const string WaldurResourceTypeHpc = "HPC";
    public const string WaldurSshUriScheme = "ssh://";
    public const string LocationLoginNodeEndpointName = "SSH login";
    public const string UnspecifiedAllocationUnitTypeName = "unspecified";

    // ── HEAppE assigned-location value keys (ApplicationConstants) ──
    public const string HeappeLocalBasePathKey = "HEAPPE_LOCAL_BASE_PATH";

    // ── Waldur role names (WaldurEfpConstants / PuhuriConstants — identical) ──
    public const string WaldurProjectManagerRoleName = "PROJECT.MANAGER";
    public const string WaldurProjectMemberRoleName = "PROJECT.MEMBER";
    public const string WaldurProjectAdminRoleName = "PROJECT.ADMIN";

    // ── Mapped Userorg role names (RoleInfoConstants) — THE persisted values (ADR-0054/A2 remarks) ──
    public const string UserorgOwnerRoleName = "Owner";
    public const string UserorgManagerRoleName = "Manager";
    public const string UserorgMemberRoleName = "Member";

    // ── Deucalion cluster hack (WaldurEfpExporter.ExtractAllocationUnitAndRemaining) ──
    public const string DeucalionLoginHost = "login.deucalion.macc.fccn.pt";
}