namespace UserOrganizationManagement.Domain.Exceptions;

public class ProjectAlreadyHasAssignedNonSharedResourceException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public ProjectAlreadyHasAssignedNonSharedResourceException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public ProjectAlreadyHasAssignedNonSharedResourceException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public ProjectAlreadyHasAssignedNonSharedResourceException()
    {
    }
    #endregion
}