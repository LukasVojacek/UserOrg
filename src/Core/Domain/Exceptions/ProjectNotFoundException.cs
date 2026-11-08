using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Project was not found exception
/// </summary>
public class ProjectNotFoundException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public ProjectNotFoundException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public ProjectNotFoundException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public ProjectNotFoundException()
    {
    }
    #endregion

    public static void ThrowIfNull([NotNull] Project? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            Throw(paramName);
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new ProjectNotFoundException(paramName);

}