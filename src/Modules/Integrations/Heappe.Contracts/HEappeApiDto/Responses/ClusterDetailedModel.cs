namespace Heappe.Contracts.HEappeApiDto.Responses;

public sealed record ClusterDetailedModel
(
    long Id,
    string Name,
    string? Description,
    string MasterNodeName,
    int SchedulerType,
    string TimeZone,
    int? Port,
    int ConnectionProtocol,
    bool UpdateJobStateByServiceAccount,
    string? DomainName,
    ProxyConnectionModel? ProxyConnection,
    IList<int> FileTransferMethodIds,
    IList<NodeTypeModel> NodeTypes
);