namespace UserOrg.Shared.Persistance;

/// <summary>
/// Provides a collection of constant values used throughout the database operations, including maximum lengths for
/// various fields and SQL expressions for generating timestamps and GUIDs.
/// </summary>
/// <remarks>These constants are intended to standardize the lengths and SQL expressions used in database
/// interactions, ensuring consistency and preventing errors related to field size limitations.</remarks>
public static class DbConstants
{
    public const int DateTimePrecision = 3;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 150;
    public const int ExtendedDescriptionMaxLength = 500;
    public const int UserNameMaxLength = 100;
    public const int ProjectShortNameMaxLength = 50;
    public const int EmailMaxLength = 100;

    public const int ValueMaxLength = 40;
    public const int ExtendedValueMaxLength = 1500;
    public const int DataMaxLength = 4000;

    // AAI module specific lengths
    public const int PermissionKeyMaxLength = 300;
    public const int RoleNameMaxLength = 100;
    public const int EndpointNameMaxLength = 200;
    public const int IdentityProviderUserIdMaxLength = 150;

    public const int ExternalIdMaxLength = 200;

    // Heappe module specific lengths
    public const int CommandTemplateNameMaxLength = 200;
    public const int ScriptPathMaxLength = 500;

    /// <summary>
    /// SQL expression for UTC creation timestamp default
    /// </summary>
    public const string UtcNowSql = "SYSUTCDATETIME()";

    /// <summary>
    /// Represents the SQL expression used to generate a new GUID identifier in SQL Server.
    /// </summary>
    public const string GuidIdSql = "NewId()";
}