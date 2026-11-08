namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Record not found exception
/// </summary>
public class RecordNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public RecordNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public RecordNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public RecordNotFoundException()
    {
    }
    #endregion
}