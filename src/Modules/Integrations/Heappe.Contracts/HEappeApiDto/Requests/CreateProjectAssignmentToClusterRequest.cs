namespace Heappe.Contracts.HEappeApiDto.Requests;

public record CreateProjectAssignmentToClusterRequest
(
    long ClusterId,
    long ProjectId,
    string ScratchStoragePath,
    string ProjectStoragePath
);

public sealed record CreateProjectAssignmentToClusterRequestV2
(
    long ClusterId,
    long ProjectId,
    string ScratchStoragePath,
    string ProjectStoragePath,
    int PreferredAuthType
) : CreateProjectAssignmentToClusterRequest(ClusterId, ProjectId, ScratchStoragePath, ProjectStoragePath);