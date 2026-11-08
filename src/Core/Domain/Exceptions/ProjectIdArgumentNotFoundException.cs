using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Project Id was not at excepted argument index or was missed entirely exception
/// </summary>
public class ProjectIdArgumentNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public ProjectIdArgumentNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public ProjectIdArgumentNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public ProjectIdArgumentNotFoundException()
    {
    }
    #endregion

    public static void ThrowIfNull([NotNull] ProjectId? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new ProjectIdArgumentNotFoundException(paramName);

}