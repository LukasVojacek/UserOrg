namespace Core.Domain.Project;

/// <summary>
/// Stable error codes for <see cref="ProjectErrors"/>. Public so the Core module can map a
/// domain <c>Error</c> to an HTTP status without string-matching the description.
/// </summary>
public static class ProjectErrorCodes
{
    public const string ReadOnly = "project.read_only";
    public const string Deleted = "project.deleted";
    public const string Expired = "project.expired";
    public const string CannotExpireDeleted = "project.cannot_expire_deleted";
    public const string EndDateBeforeStartDate = "project.end_date_before_start_date";
    public const string UserAlreadyAssigned = "project.user_already_assigned";
    public const string UserNotAssigned = "project.user_not_assigned";
    public const string CannotAssignEqualOrHigherRole = "project.cannot_assign_equal_or_higher_role";
    public const string DuplicateProjectOwner = "project.duplicate_owner";
    public const string OwnerCannotBeUnassigned = "project.owner_cannot_be_unassigned";
    public const string ResourceAlreadyAssigned = "project.resource_already_assigned";
    public const string ResourceNotAssigned = "project.resource_not_assigned";

    // Input-validation codes surfaced by the v2 endpoints (ADR-0020).
    public const string ShortNameInvalid = "project.short_name_invalid";
    public const string NameInvalid = "project.name_invalid";
    public const string DescriptionTooLong = "project.description_too_long";
    public const string ContactEmailInvalid = "project.contact_email_invalid";
    public const string DomainTypeNameInvalid = "project.domain_type_name_invalid";
    public const string DomainTypeNotFound = "project.domain_type_not_found";
    public const string TagNameInvalid = "project.tag_name_invalid";
    public const string UnknownRole = "project.unknown_role";
}