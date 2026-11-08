namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.UserPermission;

public record ProjectResourceDetailModel(string Name, string Description, DateTime? StartDate, DateTime? EndDate, IList<LocationDetailModel> Locations)
{
    private ProjectResourceDetailModel() : this(string.Empty, string.Empty, null, null, null) { }

    public static ProjectResourceDetailModel Empty => new();
}