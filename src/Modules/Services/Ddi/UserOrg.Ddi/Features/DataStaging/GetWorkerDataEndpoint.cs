using System.Security.Claims;

using HashiCorpVault.Contracts;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UserOrg.Ddi.Features.DataStaging;

/// <summary>
/// <c>GET /api/v2/DataStaging/WorkerData</c> — returns the calling DDI worker's private data-staging
/// key (resolved from its identity claim) together with the shared public key, read from Vault.
/// </summary>
internal static class GetWorkerDataEndpoint
{
    // OIDC standard claim carrying the DDI worker/data name (legacy used JwtRegisteredClaimNames.PreferredUsername).
    private const string PreferredUsernameClaim = "preferred_username";
    private const string PublicDataName = "PublicData";

    internal sealed record DataStagingPart(string Name, string JsonData);
    internal sealed record Response(string Name, DataStagingPart PublicData, DataStagingPart PrivateData);

    internal static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IHashiCorpVaultClient client,
        CancellationToken ct)
    {
        var dataName = user.FindFirstValue(PreferredUsernameClaim);
        if (string.IsNullOrEmpty(dataName))
        {
            // ponytail: legacy dereferenced a null claim (NRE); a missing identity claim is a 400, not a crash.
            return Results.BadRequest("Data staging name claim is missing.");
        }

        var privateDataPart = new DataStagingPart(string.Empty, string.Empty);
        if (!dataName.Equals(PublicDataName, StringComparison.OrdinalIgnoreCase))
        {
            var privateData = await client.GetServiceAccessKeyDataAsync(dataName, ct);
            if (!privateData.IsSuccess)
            {
                return Results.Problem(
                    title: "Failed to retrieve private data",
                    detail: privateData.Error.Description,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            privateDataPart = privateDataPart with { Name = dataName, JsonData = privateData.Value };
        }

        var publicData = await client.GetServiceAccessKeyDataAsync(PublicDataName, ct);
        if (!publicData.IsSuccess)
        {
            return Results.Problem(
                title: "Failed to retrieve public data",
                detail: publicData.Error.Description,
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var response = new Response(dataName, new DataStagingPart(PublicDataName, publicData.Value), privateDataPart);
        return Results.Ok(response);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetWorkerDataEndpoint()
        {
            app.MapGet("WorkerData", HandleAsync).MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("WorkerDataV2")
            .WithSummary("Get details for a worker.")
            .WithTags("DataStaging")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}