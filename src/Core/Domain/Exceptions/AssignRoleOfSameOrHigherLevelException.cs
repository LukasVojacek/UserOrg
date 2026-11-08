using System.Diagnostics.CodeAnalysis;

namespace UserOrganizationManagement.Domain.Exceptions;

public class AssignRoleOfSameOrHigherLevelException : Exception
{
    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    public AssignRoleOfSameOrHigherLevelException(string message) : base(message)
    {

    }

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="message">Message</param>
    /// <param name="innerException">Inner exception</param>
    public AssignRoleOfSameOrHigherLevelException(string message, Exception innerException) : base(message, innerException)
    {

    }
    public AssignRoleOfSameOrHigherLevelException()
    {
    }


    [DoesNotReturn]
    internal static void Throw(string? paramName) => throw new AssignRoleOfSameOrHigherLevelException(paramName);

}