namespace UserOrganizationManagement.Domain.Exceptions;

public class SystemPermissionTypeNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public SystemPermissionTypeNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public SystemPermissionTypeNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public SystemPermissionTypeNotFoundException()
    {
    }
    #endregion
}