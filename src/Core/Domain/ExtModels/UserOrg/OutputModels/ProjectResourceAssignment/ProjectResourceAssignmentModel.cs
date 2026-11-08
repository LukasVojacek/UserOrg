namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

public class ProjectResourceAssignmentModel
{
    public Guid? Id { get; set; }
    public Guid? ProjectResourceId { get; set; }
    public Guid? LocationId { get; set; }
    public int LocationTypeId { get; set; }
    public bool IsLocationShared { get; set; }
    public required string LocationName { get; set; }
    public long? AllocationAmount { get; set; }
    public int? AllocationUnitTypeId { get; set; }
    public string? AggregationName { get; set; }
    public EOperationModeResponse OperationMode { get; set; }
    public IList<KeyValuePair<string, string>>? Specifications { get; init; }
}