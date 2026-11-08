namespace UserOrganizationManagement.Domain.Exceptions;

public class SeedFailedException : Exception
{
    #region Constructors
    public SeedFailedException() : base("Seed failed")
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public SeedFailedException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public SeedFailedException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

}