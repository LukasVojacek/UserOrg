namespace UserOrganizationManagement.Domain.Exceptions;

public class InvalidEmailAddressException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public InvalidEmailAddressException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public InvalidEmailAddressException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public InvalidEmailAddressException()
    {
    }
    #endregion
}