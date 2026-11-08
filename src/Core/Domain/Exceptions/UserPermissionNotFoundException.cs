using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Domain.Exceptions;

public class UserPermissionNotFoundException : Exception
{
    #region Constructors
    public UserPermissionNotFoundException()
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public UserPermissionNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public UserPermissionNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

    public static void ThrowIfNull([NotNull] UserPermission? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new UserPermissionNotFoundException(paramName);

}