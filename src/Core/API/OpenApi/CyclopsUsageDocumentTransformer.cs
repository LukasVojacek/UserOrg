using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Advertises the Cyclops usage operation in the v2 documentation without Core serving it. The real request
/// is intercepted by the Gateway and proxied to the external Cyclops usage API (it never reaches Core), so
/// there is no v2 route — but consumers still need to discover the operation in Scalar. This transformer
/// injects the path/operation straight into the document, which also bypasses the v2 <c>ShouldInclude</c>
/// route filter. Registered on the v2-User document only.
/// </summary>
internal sealed class CyclopsUsageDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string UsagePath = "/api/Cyclops/Usage/{ProjectShortName}/{ProviderResourceName}";
    private const string TotalUsagePath = "/api/Cyclops/TotalUsage/{ProviderResourceName}";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Paths ??= new OpenApiPaths();

        document.Paths[UsagePath] = PathItem(new OpenApiOperation
        {
            // These operations are injected after OperationIdDocumentTransformer has run, so each has to
            // carry its own operationId; without one the v2-User document ships an operation with none.
            OperationId = "GetApiCyclopsUsageByProjectShortNameByProviderResourceName",
            Summary = "Get Cyclops usage for a project resource",
            Description = "Resource usage from the external Cyclops service. Served by the Gateway (authenticated and proxied to the Cyclops usage API); not implemented by Core, so it does not appear under api/v2.",
            Tags = new HashSet<OpenApiTagReference> { new("Cyclops", document) },
            Parameters =
            [
                PathParam("ProjectShortName"),
                PathParam("ProviderResourceName"),
                DateQueryParam("fromDate"),
                DateQueryParam("toDate"),
            ],
            Responses = Responses("ProjectShortName or ProviderResourceName is missing from the path.",
                "The caller lacks permission to read usage for this project resource."),
        });

        document.Paths[TotalUsagePath] = PathItem(new OpenApiOperation
        {
            OperationId = "GetApiCyclopsTotalUsageByProviderResourceName",
            Summary = "Get aggregated Cyclops usage for a resource",
            Description = "Usage of a resource across all projects, aggregated by the external Cyclops service. Served by the Gateway (authenticated and proxied to the Cyclops aggregate usage API); not implemented by Core, so it does not appear under api/v2.",
            Tags = new HashSet<OpenApiTagReference> { new("Cyclops", document) },
            Parameters =
            [
                PathParam("ProviderResourceName"),
                DateQueryParam("fromDate"),
                DateQueryParam("toDate"),
            ],
            Responses = Responses("ProviderResourceName is missing from the path.",
                "The caller lacks permission to read usage for this resource."),
        });

        return Task.CompletedTask;
    }

    private static OpenApiPathItem PathItem(OpenApiOperation operation) => new()
    {
        Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Get] = operation },
    };

    private static OpenApiResponses Responses(string badRequestDescription, string forbiddenDescription) => new()
    {
        ["200"] = new OpenApiResponse { Description = "Usage data returned by the Cyclops service." },
        ["400"] = new OpenApiResponse { Description = badRequestDescription },
        ["401"] = new OpenApiResponse { Description = "The caller is not authenticated." },
        ["403"] = new OpenApiResponse { Description = forbiddenDescription },
        ["500"] = new OpenApiResponse { Description = "The Cyclops integration is not configured, or the Cyclops service could not be reached." },
    };

    private static OpenApiParameter PathParam(string name) => new()
    {
        Name = name,
        In = ParameterLocation.Path,
        Required = true,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String },
    };

    private static OpenApiParameter DateQueryParam(string name) => new()
    {
        Name = name,
        In = ParameterLocation.Query,
        Required = false,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
    };
}