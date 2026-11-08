using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Exceptions;

public class AssignRoleHigherThanPlaformUserException : Exception
{
    #region Constructors
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public AssignRoleHigherThanPlaformUserException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public AssignRoleHigherThanPlaformUserException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public AssignRoleHigherThanPlaformUserException()
    {
    }

    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new AssignRoleOfSameOrHigherLevelException(paramName);
    #endregion
}