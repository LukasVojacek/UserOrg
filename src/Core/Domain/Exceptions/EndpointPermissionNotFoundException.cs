using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Permissions for endpoint was not found exception
/// </summary>
public class EndpointPermissionNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public EndpointPermissionNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public EndpointPermissionNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public EndpointPermissionNotFoundException()
    {
    }
    #endregion

    public static void ThrowIfNullOrEmpty([NotNull] IEnumerable<string>? neededPermissions, [CallerArgumentExpression(nameof(neededPermissions))] string? paramName = null)
    {
        if (neededPermissions is null || !neededPermissions.Any())
        {
            Throw(paramName);
        }
    }
    public static void ThrowIfNull([NotNull] IEnumerable<string>? neededPermissions, [CallerArgumentExpression(nameof(neededPermissions))] string? paramName = null)
    {
        if (neededPermissions is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new EndpointPermissionNotFoundException(paramName);

}