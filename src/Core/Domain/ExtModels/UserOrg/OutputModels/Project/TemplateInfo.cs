namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

public sealed record CommandTemplate(long Id, string Name, bool IsGeneric, IEnumerable<TemplateParameterModel> Parameters);
public sealed record NodeAllocation(string LexisResourceName, IEnumerable<CommandTemplate> CommandTemplates)
{
    public static NodeAllocation Invalid => new(string.Empty, []);
}
public sealed record NodeInfo(long NodeId, string NodeType, int? NumberOfNodes, int ClusterPerNode, int MaxWallTime, int FileTransferMethodId, IEnumerable<NodeAllocation> Allocations)
{
    public static NodeInfo Invalid => new(-1, string.Empty, -1, -1, -1, -1, []);
}
public sealed record TemplateInfoResponse(bool IsAvailable, string ClusterName, string LocationId, long? ClusterId, IEnumerable<NodeInfo> Nodes);