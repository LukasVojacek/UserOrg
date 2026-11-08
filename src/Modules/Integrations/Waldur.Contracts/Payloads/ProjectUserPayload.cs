using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// A project membership. Membership identity is <c>UserName</c> (ADR-0054/A2 — the Waldur user UUID
/// is not stored and cannot seed a cold rebuild). Only instance-owned fields are hashed
/// (<see cref="ProjectExternalId"/>, <see cref="UserName"/>, <see cref="Role"/>,
/// <see cref="HasAccess"/> — ADR-0055/A3); user-profile fields are written on create only and are
/// excluded from the hash.
/// </summary>
/// <remarks>
/// ROUND-TRIP INVARIANT (D1/D13): <see cref="Role"/> is hashed, so the value the API-side mapper
/// puts here MUST be the SAME value the DB-side rebuilder (<c>IShaMapRebuilder</c>, Phase 4) reads
/// back — i.e. the <b>mapped Userorg role</b> that is actually persisted, NOT the raw Waldur role
/// string. If the two sides disagree, every membership hashes differently on restart and the worker
/// storms <c>UserRoleChanged</c> for every user, every iteration. The Phase-4 round-trip hash test
/// is the guardian of this invariant.
/// </remarks>
/// <param name="ProjectExternalId">Waldur project UUID the user is assigned to (canonical "D" form).</param>
/// <param name="UserName">Membership identity — the Waldur username.</param>
/// <param name="Role">Mapped <b>Userorg</b> role name (the persisted value), not the raw Waldur role — see remarks.</param>
/// <param name="HasAccess">Resource access flag (read-only project access granted to all members).</param>
/// <param name="Email">Create-only user-profile field — not hashed (A3).</param>
/// <param name="CreatedAt">Audit timestamp — create only, not hashed (A1).</param>
[GenerateIdentityHash]
public sealed partial record ProjectUserPayload(
    string ProjectExternalId,
    string UserName,
    string Role,
    bool HasAccess
    )
{
    [HashIgnore]
    public string? Email { get; init; }

    [HashIgnore]
    public DateTime? CreatedAt { get; init; }
}