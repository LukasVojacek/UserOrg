namespace Core.DataAccess.Exceptions;

/// <summary>
/// Raised when the external role service fails while a repository hydrates or persists a user, or when it
/// succeeds but does not serve a role name we are configured to require. Public so the host's exception
/// handler can map it to 502 Bad Gateway, matching how endpoints that call the role service directly report
/// the same downstream failure.
/// </summary>
public sealed class RoleServiceException() : Exception("Failed to retrieve role names from the role service.");