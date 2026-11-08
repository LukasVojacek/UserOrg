namespace UserOrganizationManagement.Domain.Exceptions;

public class OutdatedDatabaseMigrationException : Exception
{
    #region Constructors
    public OutdatedDatabaseMigrationException() : base("Application and database migrations are not the same. Please update the database to the new version.")
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public OutdatedDatabaseMigrationException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public OutdatedDatabaseMigrationException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

}