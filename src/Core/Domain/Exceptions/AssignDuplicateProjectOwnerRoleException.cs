namespace UserOrganizationManagement.Domain.Exceptions;

public class AssignDuplicateProjectOwnerRoleException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public AssignDuplicateProjectOwnerRoleException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public AssignDuplicateProjectOwnerRoleException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public AssignDuplicateProjectOwnerRoleException()
    {
    }
    #endregion
}