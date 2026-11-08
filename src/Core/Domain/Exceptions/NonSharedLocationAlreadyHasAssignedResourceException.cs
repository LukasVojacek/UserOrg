namespace UserOrganizationManagement.Domain.Exceptions;

public class NonSharedLocationAlreadyHasAssignedResourceException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public NonSharedLocationAlreadyHasAssignedResourceException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public NonSharedLocationAlreadyHasAssignedResourceException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public NonSharedLocationAlreadyHasAssignedResourceException()
    {
    }
    #endregion
}