using System.Collections.Immutable;

using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// Fat payload for a project, including its marketplace resources (ADR-0054). Upsert key is
/// <c>(ExternalId, ServiceName)</c>. Removal is carried by <see cref="IsRemoved"/> (ADR-0054/A1) —
/// there is no separate delete event; the flag is part of the hash so removal is just another
/// <c>ProjectUpdated</c>.
/// </summary>
/// <param name="ExternalId">Waldur project UUID (canonical lowercase "D" form).</param>
/// <param name="Name">Project name (truncated in mapping).</param>
/// <param name="ShortName">Project slug.</param>
/// <param name="Description">Optional description (truncated in mapping).</param>
/// <param name="StartDate">Project start date (null-fallback applied in mapping).</param>
/// <param name="EndDate">Project end date (<c>?? MaxValue</c> in mapping).</param>
/// <param name="IsRemoved">Waldur <c>is_removed</c> flag — hashed; the consumer soft-deletes when set.</param>
/// <param name="Resources">Embedded eligible resources (may be empty when <see cref="IsRemoved"/> is true).</param>
/// <param name="CreatedAt">Audit timestamp — travels for create only, never hashed (A1).</param>
[GenerateIdentityHash]
public sealed partial record ProjectPayload(
    string ExternalId,
    string Name,
    string ShortName,
    string? Description,
    DateTime StartDate,
    DateTime EndDate,
    bool IsRemoved,
    IImmutableList<ResourcePayload> Resources)
{
    [HashIgnore]
    public DateTime? CreatedAt { get; init; }
}