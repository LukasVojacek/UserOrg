namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

public record LocationDetailModel(string Name, string Descritpion, string Endpoint, bool IsPrivate, string Type, long? AllocationUnitCount, string AllocationUnitType, IEnumerable<KeyValuePair<string, string>> Values)
{
    private LocationDetailModel() : this(string.Empty, string.Empty, string.Empty, false, string.Empty, null, string.Empty, []) { }
    public static LocationDetailModel Empty => new();
}