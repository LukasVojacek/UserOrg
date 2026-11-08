using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;

public class PublicRequestPrepareModel
{
    public Guid? RequestId { get; set; }
    /// <summary>
    /// The user name of the requestor.
    /// </summary>
    public string? RequestorUserName { get; set; }

    /// <summary>
    /// The email of the requestor.
    /// </summary>
    public string? RequestorEmail { get; set; }
    public ProjectShortName ProjectShortName { get; set; }
    public string? ProjectResourceName { get; set; }
    public string? ProviderName { get; set; }
    public int RequestState { get; set; }
    public DateTime ValidTo { get; set; }
    public int RequestType { get; set; }
    public EActionType Action { get; set; }
    public string? ActionLink { get; set; }
    public bool IsDeleted { get; set; }
}

public class PublicRequestModel
{
    public Guid? RequestId { get; set; }
    /// <summary>
    /// The user name of the requestor.
    /// </summary>
    public string? RequestorUserName { get; set; }

    /// <summary>
    /// The email of the requestor.
    /// </summary>
    public string? RequestorEmail { get; set; }
    public ProjectShortName ProjectShortName { get; set; }
    public string? ProjectResourceName { get; set; }
    public string? ProviderName { get; set; }
    public int RequestState { get; set; }
    public DateTime ValidTo { get; set; }
    public int RequestType { get; set; }
    public string? AcceptActionLink { get; set; }
    public string? RejectActionLink { get; set; }
}