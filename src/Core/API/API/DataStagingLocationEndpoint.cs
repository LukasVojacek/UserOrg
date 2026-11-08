using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Microsoft.AspNetCore.Mvc;

namespace UserOrganizationManagement.API.API;

internal static class DataStagingLocationEndpoint
{
    public const string authorizationHttpHeaderName = "X-Api-DataStaging";

    internal sealed record DataStagingPart(string Name, string JsonData);
    internal sealed record DataStagingDataModel(string Name, DataStagingPart PublicData, DataStagingPart PrivateData);

    public static void RegisterDataStagingLocationEndpoint(this WebApplication app)
    {
        var group = app.MapGroup("api/DataStaging")
          .WithTags("DataStaging");

        group.MapGet("WorkerData", async (HttpContext context, [FromServices] IHashiCorpVaultClient client, CancellationToken ct) =>
        {
            var dataName = context.User.FindFirstValue(JwtRegisteredClaimNames.PreferredUsername);
            // TODO: move to config
            var publicDataName = "PublicData";
            var privateDataPart = new DataStagingPart(string.Empty, string.Empty);

            if (!dataName.Equals(publicDataName, StringComparison.OrdinalIgnoreCase))
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

            var publicData = await client.GetServiceAccessKeyDataAsync(publicDataName, ct);
            if (!publicData.IsSuccess)
            {
                return Results.Problem(
                    title: "Failed to retrieve public data",
                    detail: publicData.Error.Description,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var model = new DataStagingDataModel(dataName, new(publicDataName, publicData.Value), privateDataPart);

            return Results.Ok(model);

        }).WithName("WorkerData")
          .WithDescription("Get details for a worker.")
          .Produces<DataStagingDataModel>(StatusCodes.Status200OK)
          .ProducesValidationProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get details for a worker.""";
              generatedOperation.Description = "Get details for a worker.";
              return generatedOperation;
          });

        group.MapGet("DataStagingLocations", async ([FromServices] IHashiCorpVaultClient client, CancellationToken ct) =>
        {
            var res = await client.GetDDIDataStagingKeysAsync(ct);
            if (!res.IsSuccess)
            {
                return Results.Problem(title: "Failed to retrieve DDI entries", detail: res.Error?.Description, statusCode: StatusCodes.Status500InternalServerError);
            }
            return Results.Ok(res.Value);
        })
        .WithName("GetDataStagingLocations")
        .WithDescription("List DDI DataStaging locations from Vault")
        .Produces<IEnumerable<DataStagingLocationKey>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(op =>
        {
            op.Summary = "List DDI DataStaging locations";
            op.Description = "Reads all DataStagingLocationKey entries for DDI from Vault";
            return op;
        });

        group.MapPost("DataStagingLocations", async (HttpContext context, IHashiCorpVaultClient vaultClient, CancellationToken ct) =>
        {
            // Accept either an array of DataStagingLocationKey or an object whose properties are named entries
            using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: ct);
            var root = doc.RootElement;

            List<DataStagingLocationKey> itemsList = [];
            if (root.ValueKind == JsonValueKind.Array)
            {
                var items = JsonSerializer.Deserialize<IEnumerable<DataStagingLocationKey>>(root.GetRawText());
                if (items is not null)
                {
                    itemsList.AddRange(items);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    try
                    {
                        DataStagingLocationKey? item = null;
                        if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                        {
                            item = JsonSerializer.Deserialize<DataStagingLocationKey>(prop.Value.GetRawText());
                        }
                        if (item is null)
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(item.Name))
                        {
                            item.Name = prop.Name;
                        }
                        itemsList.Add(item);
                    }
                    catch (JsonException)
                    {
                        continue;
                    }
                }
            }
            else
            {
                return Results.BadRequest("Invalid payload");
            }

            if (itemsList.Count == 0)
            {
                return Results.BadRequest("Invalid payload");
            }

            var result = await vaultClient.UpdateServiceAccessKeysBulkAsync(itemsList, ct);
            if (!result.IsSuccess)
            {
                return Results.Problem(result.Error?.ToString() ?? "Vault update failed");
            }

            return Results.Ok(result.Value);
        })
       .WithName("UpdateDataStagingLocationKeys")
       .WithDescription("Bulk update of DataStagingLocationKeys in Vault")
       .Accepts<IEnumerable<DataStagingLocationKey>>("application/json")
       .Accepts<string>("application/json")
       .Produces<BulkUpdateResult>(StatusCodes.Status200OK)
       .ProducesProblem(StatusCodes.Status400BadRequest)
       .ProducesProblem(StatusCodes.Status500InternalServerError)
       .WithOpenApiDoc(generatedOperation =>
       {
           generatedOperation.Summary = "Bulk update DataStagingLocationKeys for DDI";
           generatedOperation.Description = "Bulk update DataStagingLocationKeys for DDI";
           return generatedOperation;
       });

    }
}