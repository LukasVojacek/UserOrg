using System.Text.Json;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace UserOrg.Ddi.Features.DataStaging;

/// <summary>
/// <c>POST /api/v2/DataStaging/DataStagingLocations</c> — bulk create/update/delete of DDI data-staging
/// location keys in Vault. Accepts either a JSON array of keys or a JSON object whose properties are
/// named entries (legacy parity).
/// </summary>
internal static class UpdateDataStagingLocationsEndpoint
{
    internal static async Task<IResult> HandleAsync(HttpContext context, IHashiCorpVaultClient client, CancellationToken ct)
    {
        var items = await ParseItemsAsync(context.Request.Body, ct);
        if (items is null || items.Count == 0)
        {
            return Results.BadRequest("Invalid payload");
        }

        var result = await client.UpdateServiceAccessKeysBulkAsync(items, ct);
        if (!result.IsSuccess)
        {
            return Results.Problem(result.Error?.ToString() ?? "Vault update failed");
        }

        return Results.Ok(result.Value);
    }

    // Returns null when the payload is malformed JSON or neither an array nor an object; an empty list
    // for an empty/whitespace-only set. Malformed object entries are skipped (legacy parity). A null
    // return maps to 400 in HandleAsync — invalid input is a client error, not a 500.
    private static async Task<List<DataStagingLocationKey>?> ParseItemsAsync(Stream body, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<IEnumerable<DataStagingLocationKey>>(root.GetRawText());
                return items is null ? [] : [.. items];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ParseObjectEntries(root);
        }
        catch (JsonException)
        {
            // Malformed document or array element. The object branch already tolerates bad entries
            // per-entry; a parse- or array-level failure was an unhandled 500 before — now a 400.
            return null;
        }
    }

    private static List<DataStagingLocationKey> ParseObjectEntries(JsonElement root)
    {
        List<DataStagingLocationKey> result = [];
        foreach (var prop in root.EnumerateObject())
        {
            try
            {
                if (prop.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.String))
                {
                    continue;
                }

                var item = JsonSerializer.Deserialize<DataStagingLocationKey>(prop.Value.GetRawText());
                if (item is null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(item.Name))
                {
                    item.Name = prop.Name;
                }

                result.Add(item);
            }
            catch (JsonException)
            {
                // skip malformed entries (legacy parity)
            }
        }

        return result;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateDataStagingLocationsEndpoint()
        {
            app.MapPost("DataStagingLocations", HandleAsync).MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateDataStagingLocationKeysV2")
            .WithSummary("Bulk update DataStagingLocationKeys for DDI.")
            .WithTags("DataStaging")
            .Accepts<IEnumerable<DataStagingLocationKey>>("application/json")
            .Produces<BulkUpdateResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }
}