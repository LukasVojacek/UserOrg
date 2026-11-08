using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Domain.Exceptions;

public class ProviderNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public ProviderNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public ProviderNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public ProviderNotFoundException()
    {
    }
    #endregion

    public static void ThrowIfNull([NotNull] Provider? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new ProviderNotFoundException(paramName);

}