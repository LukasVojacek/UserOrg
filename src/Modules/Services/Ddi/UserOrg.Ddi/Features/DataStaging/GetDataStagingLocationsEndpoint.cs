using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UserOrg.Ddi.Features.DataStaging;

/// <summary>
/// <c>GET /api/v2/DataStaging/DataStagingLocations</c> — lists all DDI data-staging location keys
/// held in Vault.
/// </summary>
internal static class GetDataStagingLocationsEndpoint
{
    internal static async Task<IResult> HandleAsync(IHashiCorpVaultClient client, CancellationToken ct)
    {
        var result = await client.GetDDIDataStagingKeysAsync(ct);
        if (!result.IsSuccess)
        {
            return Results.Problem(
                title: "Failed to retrieve DDI entries",
                detail: result.Error?.Description,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(result.Value);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetDataStagingLocationsEndpoint()
        {
            app.MapGet("DataStagingLocations", HandleAsync).MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetDataStagingLocationsV2")
            .WithSummary("List DDI DataStaging locations from Vault.")
            .WithTags("DataStaging")
            .Produces<IReadOnlyCollection<DataStagingLocationKey>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}