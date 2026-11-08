namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record NodeTypeModel
(
    long Id,
    string Name,
    string Description,
    int NumberOfNodes,
    int CoresPerNode,
    int MaxWalltime,
    int FileTransferMethodId,
    string Queue,
    string QualityOfService,
    string ClusterAllocationName,
    ClusterNodeTypeAggregationModel? ClusterNodeTypeAggregation,
    IList<AccountingModel> Accounting,
    IList<ProjectModel> Projects
)
{
    public static NodeTypeModel MigrationInstance(long id, string Name) => new(
        Id: id,
        Name: Name,
        Description: string.Empty,
        NumberOfNodes: 0,
        CoresPerNode: 0,
        MaxWalltime: 0,
        FileTransferMethodId: 0,
        Queue: string.Empty,
        QualityOfService: string.Empty,
        ClusterAllocationName: string.Empty,
        ClusterNodeTypeAggregation: ClusterNodeTypeAggregationModel.Empty,
        Accounting: Array.Empty<AccountingModel>(),
        Projects: Array.Empty<ProjectModel>()
    );
}