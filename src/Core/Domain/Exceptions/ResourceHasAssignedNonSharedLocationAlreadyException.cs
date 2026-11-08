namespace UserOrganizationManagement.Domain.Exceptions;

public class ResourceHasAssignedNonSharedLocationAlreadyException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public ResourceHasAssignedNonSharedLocationAlreadyException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public ResourceHasAssignedNonSharedLocationAlreadyException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public ResourceHasAssignedNonSharedLocationAlreadyException()
    {
    }
    #endregion
}