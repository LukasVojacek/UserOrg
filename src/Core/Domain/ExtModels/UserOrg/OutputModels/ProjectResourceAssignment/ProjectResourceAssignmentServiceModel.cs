namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

public class ProjectResourceAssignmentServiceModel
{
    public string? LocationName { get; set; }
    public string? LocationDescription { get; set; }
    public string? ResourceType { get; set; }
    public long? AllocationAmount { get; set; }
    public string? AllocationUnitTypeName { get; set; }
    public string? AggregationName { get; set; }
    public IList<KeyValuePair<string, string>>? Specifications { get; init; }
}