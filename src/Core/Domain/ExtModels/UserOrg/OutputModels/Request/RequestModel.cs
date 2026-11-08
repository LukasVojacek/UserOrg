using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;

public class RequestModel
{
    public Guid? RequestId { get; set; }
    public string? IdpUserId { get; set; }
    /// <summary>
    /// The user name of the requestor.
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// The email of the requestor.
    /// </summary>
    public string? Email { get; set; }
    public ProjectShortName? ProjectShortName { get; set; }
    public int? State { get; set; }
    public int Type { get; set; }
    public DateTime LastModification { get; set; }
}