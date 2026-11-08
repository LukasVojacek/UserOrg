namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

public class ProjectResourceAssignmentExtendedModel
{
    public string? ProjectResourceName { get; set; }
    public string? LocationName { get; set; }
    public long? AllocationAmount { get; set; }
    public string? AllocationUnitTypeName { get; set; }
    public string? AggregationName { get; set; }
    public IList<KeyValuePair<string, string>>? Specifications { get; init; }
    public int LocationTypeId { get; set; }
}