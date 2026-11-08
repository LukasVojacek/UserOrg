namespace UserOrganizationManagement.Domain.Exceptions;

/// <summary>
/// Thrown when a project-level precondition for a TemplatesInfo build fails on our side — currently the read
/// of the project's active command templates from the Heappe integration database. Distinct from
/// <see cref="HeappeUnavailableException"/> (a remote HEAppE outage, 503): this is an internal fault, surfaced
/// to API callers as HTTP 500 so a DB problem is not mislabeled as HEAppE being unavailable (ADR 0074).
/// </summary>
public class TemplatesInfoPreconditionException : Exception
{
    public TemplatesInfoPreconditionException(string message) : base(message)
    {
    }

    public TemplatesInfoPreconditionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public TemplatesInfoPreconditionException()
    {
    }
}