namespace UserOrg.Shared;

/// <summary>
/// Provides constant values for application-wide configuration keys and default service identifiers.
/// </summary>
/// <remarks>This static class contains string constants used throughout the application for configuration and
/// database operations. These constants help ensure consistency when referencing service names and configuration keys,
/// particularly for HEAppE integration.</remarks>
public static class ApplicationConstants
{
    /// <summary>
    /// Represents the default service name used when storing or retrieving service-related data from the database.
    /// </summary>
    public const string DefaultServiceNameInDatabase = "LEXIS";

    /// <summary>
    /// Represents a not defined or not applicable value.
    /// </summary>
    public const string NotDefined = "N/A";

    /// <summary>
    /// Represents the configuration key used to retrieve the HEAppE service endpoint URI.
    /// </summary>
    /// 
    public const string HeappeUriKey = "HEAPPE_URL";
    /// <summary>
    /// Represents the configuration key used to specify the HEAppE version.
    /// </summary>
    public const string HeappeVersionKey = "HEAPPE_VERSION";

    /// <summary>
    /// Represents the key used to identify the HEAPPE identifier in configuration or data structures.
    /// </summary>
    public const string HeappeIdentifierKey = "HEAPPE_IDENTIFIER";

    /// <summary>
    /// Represents the configuration key for specifying the local base path used by HEAppE integrations.
    /// </summary>
    /// <remarks>Use this key to retrieve or set the local base directory path for HEAppE-related operations
    /// in configuration providers. The value should be a valid file system path accessible by the
    /// application.</remarks>
    public const string HEAppELocalBasePathKey = "HEAPPE_LOCAL_BASE_PATH";
}