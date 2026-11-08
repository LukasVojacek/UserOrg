using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace UserOrganizationManagement.Domain.Exceptions;

public class DatabaseCommitException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public DatabaseCommitException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public DatabaseCommitException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public DatabaseCommitException()
    {
    }
    #endregion

    public static void ThrowIfZero([NotNull] int savedChanges, [CallerArgumentExpression(nameof(savedChanges))] string? paramName = null)
    {
        if (savedChanges == 0)
        {
            Throw($"{paramName} was Zero");
        }
    }

    public static void ThrowIfLessThan(int exceptedChanges, [NotNull] int savedChanges, [CallerArgumentExpression(nameof(savedChanges))] string? paramName = null)
    {
        if (savedChanges < exceptedChanges)
        {
            Throw($"{paramName} was less then {exceptedChanges}");
        }
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new DatabaseCommitException(paramName);

}