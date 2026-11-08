using UserOrg.Shared;

using UserOrganizationManagement.Domain.Contracts;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;

public record ProjectHEAppELinks : IProjectHEAppELinks
{
    public string? ResourceName { get; set; }
    public Uri? HEAppEApi { get; set; }
    public Version? HEAppEApiVersion { get; set; }
    /// <summary>The <c>HEAPPE_IDENTIFIER</c> the link is routed through NexusApi by; <c>null</c> when the location carries no valid one, and the link is skipped.</summary>
    public HeappeIdentifier? HEAppEApiIdentifier { get; set; }
    public string? ProviderName { get; set; }
    public string? LocationName { get; set; }
    public string? LocationId { get; set; }
    public string? ResponseMessage { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime ProjectStartDate { get; set; }
    public DateTime ProjectEndDate { get; set; }
    public DateTime? ResourceStartDate { get; set; }
    public DateTime? ResourceEndDate { get; set; }
    public DateTime StartDate => ProjectStartDate <= ResourceStartDate.GetValueOrDefault(DateTime.MaxValue) ? ProjectStartDate : ResourceStartDate.Value;
    public DateTime EndDate => ProjectEndDate <= ResourceEndDate.GetValueOrDefault(DateTime.MaxValue) ? ProjectEndDate : ResourceEndDate.Value;
    public List<ClusterInfoModel> ExtractedData { get; set; } = [];
}

public class ClusterInfoModel
{
    /// <summary>
    /// Cluster Id
    /// </summary>
    public long Id { get; set; }
    public string? LocationId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public IEnumerable<NodeTypeModel>? NodeTypes { get; set; }
}

public class NodeTypeModel
{
    /// <summary>
    /// Node Id
    /// </summary>
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int NumberOfNodes { get; set; }
    public int CoresPerNode { get; set; }
    public int MaxWalltime { get; set; }
    public int FileTransferMethodId { get; set; }
    public IEnumerable<HEAppEProjectModel>? Projects { get; set; }
}

public class HEAppEProjectModel
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? AccountingString { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public IEnumerable<CommandtemplateModel>? CommandTemplates { get; set; }
}

public class CommandtemplateModel
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public object? ExtendedAllocationCommand { get; set; }
    public bool IsGeneric { get; set; }
    public IEnumerable<TemplateParameterModel>? TemplateParameters { get; set; }
}

public class TemplateParameterModel
{
    public string? Identifier { get; set; }
    public string? Description { get; set; } = "placeholder";
}