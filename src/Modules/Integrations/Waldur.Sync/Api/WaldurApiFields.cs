namespace Waldur.Sync.Api;

/// <summary>
/// The exact <c>field</c> selectors sent to each Waldur endpoint so the API returns only the columns
/// the sync needs. Ported verbatim from the legacy <c>ICollectorService.Required*Fields</c>.
/// </summary>
internal static class WaldurApiFields
{
    public static readonly IReadOnlyList<string> Customer =
    [
        "uuid", "name", "display_name", "description", "country", "created"
    ];

    public static readonly IReadOnlyList<string> MarketplaceResource =
    [
        "uuid", "name", "state", "description", "limits", "project_uuid",
        "offering_uuid", "offering_name", "backend_id", "effective_id",
        "end_date", "created", "modified"
    ];

    public static readonly IReadOnlyList<string> PublicOffering =
    [
        "uuid", "name", "description", "endpoints", "components", "customer_uuid",
        "created", "state", "slug", "category_title", "shared", "plugin_options"
    ];

    public static readonly IReadOnlyList<string> PublicOfferingDetail =
    [
        "uuid", "name", "slug", "plugin_options"
    ];

    public static readonly IReadOnlyList<string> Project =
    [
        "uuid", "name", "slug", "description", "start_date", "end_date", "created", "is_removed"
    ];
}