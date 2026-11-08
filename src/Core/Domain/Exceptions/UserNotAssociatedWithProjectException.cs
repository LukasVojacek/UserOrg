using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Exceptions;

public class UserNotAssociatedWithProjectException : Exception
{
    #region Constructors
    public UserNotAssociatedWithProjectException()
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public UserNotAssociatedWithProjectException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public UserNotAssociatedWithProjectException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new UserNotAssociatedWithProjectException(paramName);
}