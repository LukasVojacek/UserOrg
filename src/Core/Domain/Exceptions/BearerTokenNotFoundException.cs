namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Record not found exception
/// </summary>
public class BearerTokenNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public BearerTokenNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public BearerTokenNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public BearerTokenNotFoundException()
    {
    }
    #endregion
}