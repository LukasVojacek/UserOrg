namespace Core.Domain.Project;

/// <summary>
/// Stable error codes for <see cref="ProjectTagErrors"/>. Public so the Core module can map a domain
/// <c>Error</c> to an HTTP status without string-matching the description.
/// </summary>
public static class ProjectTagErrorCodes
{
    public const string TagNotFound = "project_tag.not_found";
    public const string TagInUse = "project_tag.in_use";
    public const string AlreadyDeleted = "project_tag.already_deleted";
}