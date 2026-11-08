using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Role was not found exception
/// </summary>
public class SystemRoleNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public SystemRoleNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public SystemRoleNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public SystemRoleNotFoundException()
    {
    }
    #endregion

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new SystemRoleNotFoundException(paramName);

}