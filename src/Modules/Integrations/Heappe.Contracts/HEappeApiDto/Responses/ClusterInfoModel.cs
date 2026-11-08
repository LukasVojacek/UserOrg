namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ClusterInfoModel
(
    long Id,
    string Name,
    string? Description,
    IList<int> FileTransferMethodIds,
    IList<NodeTypeModel> NodeTypes
)
{
    public static ClusterInfoModel MapFrom(ClusterDetailedModel detailedModel) => new(
            detailedModel.Id,
            detailedModel.Name,
            detailedModel.Description,
            detailedModel.FileTransferMethodIds,
            detailedModel.NodeTypes
        );

}