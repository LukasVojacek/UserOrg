namespace UserOrganizationManagement.Domain.Enums;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1008:Enums should have zero value", Justification = "<Pending>")]
public enum ERequestState
{
    Pending = 1,
    PendingForPIAccept = 2,
    PendingForHPCProject = 3,
    Accepted = 4,
    Rejected = 5,
}