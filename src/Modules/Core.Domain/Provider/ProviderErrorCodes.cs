namespace Core.Domain.Provider;

/// <summary>
/// Stable error codes for <see cref="ProviderErrors"/>. Public so the Core module can map a
/// domain <c>Error</c> to an HTTP status without string-matching the description (the
/// <c>Lukdrasil.Result</c> <c>State</c> enum has no Conflict member, so 409 cannot come from
/// <c>ToHttpResult()</c>).
/// </summary>
public static class ProviderErrorCodes
{
    public const string AlreadyDeactivated = "provider.already_deactivated";
    public const string NotDeactivated = "provider.not_deactivated";
    public const string NameAlreadyExists = "provider.name_already_exists";
    public const string LocationAlreadyExists = "provider.location_already_exists";
    public const string LocationNotFound = "provider.location_not_found";
    public const string LocationAlreadyDeleted = "provider.location_already_deleted";
    public const string AssignedLocationNotFound = "provider.assigned_location_not_found";
    public const string AssignedLocationAlreadyDeleted = "provider.assigned_location_already_deleted";
    public const string ProviderReadOnly = "provider.read_only";
    public const string LocationReadOnly = "provider.location_read_only";
    public const string AssignmentReadOnly = "provider.assignment_read_only";
    public const string ResourceAlreadyHasNonSharedLocation = "provider.resource_already_has_nonshared_location";
    public const string NonSharedLocationAlreadyHasResource = "provider.nonshared_location_already_has_resource";
    public const string ResourceNotUnderProvider = "provider.resource_not_under_provider";
    public const string NoLocationAssignments = "provider.no_location_assignments";

    // Input-validation (400) codes for the v2 endpoints (ADR-0020). Field-specific so a client can
    // branch on the code rather than the human-readable message.
    public const string InvalidName = "provider.invalid_name";
    public const string InvalidLocationName = "provider.invalid_location_name";
    public const string InvalidEndpoint = "provider.invalid_endpoint";
    public const string CountryNotFound = "provider.country_not_found";
    public const string InvalidLocationType = "provider.invalid_location_type";
}