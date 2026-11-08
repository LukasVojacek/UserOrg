using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Domain.Exceptions;

public class RequestException : Exception
{
    #region Constructors
    public RequestException()
    {
    }
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public RequestException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public RequestException(string message, Exception innerException) : base(message, innerException)
    {

    }
    #endregion

    public static void ThrowIfNull([NotNull] Request? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new RequestException(paramName);

}