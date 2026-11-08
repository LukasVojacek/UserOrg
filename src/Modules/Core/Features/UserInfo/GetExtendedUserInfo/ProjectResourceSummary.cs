using EAllocationUnitType = UserOrg.Shared.Persistance.Abstractions.Enums.EAllocationUnitType;

namespace Core.Features.UserInfo.GetExtendedUserInfo;

internal sealed record ProjectResourceSummary(
    string ResourceName,
    string? Description,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    IReadOnlyList<ClusterInfo> Clusters);

internal sealed record ClusterInfo(
    string Name,
    string? Description,
    string Url,
    bool IsPrivate,
    string ClusterType,
    long? AllocationUnitCount,
    EAllocationUnitType AllocationUnit,
    IReadOnlyList<ClusterAttribute> Attributes);

internal sealed record ClusterAttribute(string Name, string Value);