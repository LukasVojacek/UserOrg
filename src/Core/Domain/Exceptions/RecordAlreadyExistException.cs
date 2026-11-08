namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Record already exist exception
/// </summary>
public class RecordAlreadyExistException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public RecordAlreadyExistException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public RecordAlreadyExistException(string message, Exception innerException) : base(message, innerException)
    {

    }

    public RecordAlreadyExistException()
    {
    }
    #endregion
}