using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class OtherEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Other")
            .WithTags("OtherManagement");

        group.MapGet("Country", async ([FromQuery(Name = "CountryCode")] string? countryCode, [FromServices] IOtherService service, [FromServices] IValidator<CountryRequestModel> validator, CancellationToken ct) =>
        {
            var model = new CountryRequestModel(countryCode);
            validator.ValidateAndThrow(model);

            return Results.Ok(await service.GetCountriesAsync(countryCode, ct));

        }).Produces<IEnumerable<CountryModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = "Get list of countries";
              generatedOperation.Description = "Get lists of all countries";
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "CountryCode";
              return generatedOperation;
          });

        group.MapGet("Statistics", async ([FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, [FromServices] IOtherService service, [FromServices] IValidator<CountryRequestModel> validator, CancellationToken ct) =>
        {
            DateOnly requestDate = fromDate ?? new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

            toDate ??= DateOnly.FromDateTime(DateTime.Today);

            if (toDate <= requestDate)
            {
                toDate = requestDate.AddDays(7);
            }

            return Results.Ok(await service.GetWeeklyStatistics(requestDate, toDate.Value, ct));

        }).Produces<IEnumerable<StatisticsModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = "Get weekly statistics";
              generatedOperation.Description = "weekly statistics";
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "Specify the start date for the statistics. If not provided, the default is the current month.";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "Specify the end date for the statistics. If not provided, the default is today. An end date that is not after the start date is moved to seven days after it.";
              return generatedOperation;
          });

        group.MapGet("CyclopsUsage/{ProjectShortName}/{ResourceName}", async (
            [FromQuery] DateOnly? fromDate,
            [FromQuery] DateOnly? toDate,
            [FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName,
            [FromRoute(Name = "ResourceName")] string resourceName,
            [FromServices] IOtherService service, CancellationToken ct) =>
        {
            return Results.Accepted();
        })
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = "Get Cyclops usage for a project resource";
              generatedOperation.Description = "Resource usage for one LEXIS Project Resource, taken from the external Cyclops accounting service. The request never reaches this handler in a deployed system: the Gateway intercepts every /api/Other/CyclopsUsage/ path, checks the caller's permissions, resolves the project short name to a Cyclops customer id, forwards fromDate and toDate as the Cyclops from/to window together with that customer id, and returns the Cyclops response. It answers 400 when the project and resource segments are missing, 404 when no Cyclops customer matches the project, and 500 when the Cyclops credentials or the Cyclops customer lookup are unavailable. This route exists in Core only so the operation is published and RBAC-mapped; called directly it accepts the request and returns no content.";
              Describe(generatedOperation, "ProjectShortName", "LEXIS Project Short-Name. Resolved by the Gateway to the matching Cyclops customer; the match is by customer name and is case-insensitive.");
              Describe(generatedOperation, "ResourceName", "LEXIS Project Resource name.");
              Describe(generatedOperation, "fromDate", "Start of the usage window, forwarded to Cyclops as 'from'. Omit for the Cyclops default window.");
              Describe(generatedOperation, "toDate", "End of the usage window, forwarded to Cyclops as 'to'. Omit for the Cyclops default window.");
              return generatedOperation;
          });
    }

    /// <summary>
    /// Sets a parameter's description by NAME. The Cyclops route mixes route and query parameters, whose order
    /// in the generated operation does not follow the handler's signature, so a positional index would be a
    /// standing trap.
    /// </summary>
    private static void Describe(OpenApiOperation operation, string name, string description)
    {
        var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == name);
        if (parameter is not null)
        {
            parameter.Description = description;
        }
    }
}