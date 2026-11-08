namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ProjectAssignmentToClusterModel
(
    long ClusterId,
    long ProjectId,
    string ScratchStoragePath,
    string ProjectStoragePath,
    DateTime CreatedAt,
    DateTime? ModifiedAt
);