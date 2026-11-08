namespace UserOrganizationManagement.Domain.Configuration;

/// <summary>
/// Configuration for the Scalar API reference UI and related OpenAPI metadata.
/// </summary>
public sealed class ScalarUiOptions
{
    public const string SectionName = "Scalar";

    private string _publicPathBase = string.Empty;
    private string _routePrefix = "api";
    private string _serverUrl = string.Empty;

    /// <summary>
    /// Gets or sets the Scalar UI title shown in the page header.
    /// </summary>
    public string Title { get; set; } = "UserOrg API";

    /// <summary>
    /// Gets or sets the OpenAPI description shown in the API reference.
    /// </summary>
    public string Description { get; set; } = "UserOrg Management API";

    /// <summary>
    /// Gets or sets the route prefix where Scalar and OpenAPI documents are exposed.
    /// </summary>
    public string PublicPathBase
    {
        get => _publicPathBase;
        set => _publicPathBase = value?.Trim('/') ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the route prefix where Scalar and OpenAPI documents are exposed.
    /// </summary>
    public string RoutePrefix
    {
        get => _routePrefix;
        set => _routePrefix = string.IsNullOrWhiteSpace(value) ? "api" : value.Trim('/');
    }

    /// <summary>
    /// Gets or sets the contact name published in <c>info.contact</c>.
    /// </summary>
    /// <remarks>Defaults taken from the repository README ("Contact" section).</remarks>
    public string ContactName { get; set; } = "IT4Innovations";

    /// <summary>
    /// Gets or sets the contact e-mail published in <c>info.contact</c>.
    /// </summary>
    public string ContactEmail { get; set; } = "support@lexis.tech";

    /// <summary>
    /// Gets or sets the contact URL published in <c>info.contact</c>.
    /// </summary>
    public string ContactUrl { get; set; } = "https://opencode.it4i.eu/lexis-platform/backend/userorg";

    /// <summary>
    /// Gets or sets the licence name published in <c>info.license</c>.
    /// </summary>
    /// <remarks>Defaults to the licence in the repository root LICENSE file (Apache License 2.0).</remarks>
    public string LicenseName { get; set; } = "Apache-2.0";

    /// <summary>
    /// Gets or sets the licence URL published in <c>info.license</c>.
    /// </summary>
    public string LicenseUrl { get; set; } = "https://www.apache.org/licenses/LICENSE-2.0";

    /// <summary>
    /// Gets or sets the optional public server URL used in the generated OpenAPI document.
    /// </summary>
    public required string ServerUrl
    {
        get => _serverUrl;
        set => _serverUrl = value?.TrimEnd('/') ?? string.Empty;
    }
}