using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Domain.Exceptions;

public class RequestStateIsNotPendingException : Exception
{
    #region Constructors
    public RequestStateIsNotPendingException()
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public RequestStateIsNotPendingException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public RequestStateIsNotPendingException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

    public static void ThrowIfNotPending([NotNull] Request argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument.State > ERequestState.PendingForHPCProject)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new RequestStateIsNotPendingException(paramName);

}