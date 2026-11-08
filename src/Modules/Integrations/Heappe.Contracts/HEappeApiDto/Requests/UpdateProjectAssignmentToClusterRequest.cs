namespace Heappe.Contracts.HEappeApiDto.Requests;

public sealed record UpdateProjectAssignmentToClusterRequest
(
    long ClusterId,
    long ProjectId,
    string ScratchStoragePath,
    string ProjectStoragePath
);