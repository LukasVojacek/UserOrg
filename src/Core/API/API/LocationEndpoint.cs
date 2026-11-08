
using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;

namespace UserOrganizationManagement.API.API;

internal sealed class LocationEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Location")
            .WithTags("LocationManagement");

        group.MapGet("", async ([FromQuery(Name = "ProviderName")] string? providerName, [FromQuery(Name = "LocationTypeId")] int? locationTypeId, [FromServices] ILocationService service, [FromServices] IUserService userService, [FromServices] IValidator<LocationRequestModel> validator, HttpContext httpContext, CancellationToken ct) =>
        {
            var model = new LocationRequestModel(providerName, locationTypeId);
            validator.ValidateAndThrow(model);

            if (!httpContext.TryGetOpenIdUserInfoModel(out var openIdModel))
            {
                return Results.Problem("Invalid token: failed to retrieve user info", statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Ok(await service.GetRelatedLocationAsync(openIdModel.Sub, openIdModel.ProviderId, providerName, locationTypeId, ct));

        }).Produces<IEnumerable<LocationModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Locations" """;
              generatedOperation.Description = """Get list of all "LEXIS Locations" <br>Locations types: {HPC - 1, Cloud - 2, K8s - 3, Storage -4, iRODS -5, FirecREST -6, Quantum -7}""";
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              var parameter2 = generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Provider Name";
              //var parameter3 = generatedOperation.Parameters[2];
              //parameter3.Description = "LEXIS Location Type Id";
              return generatedOperation;
          });

        group.MapGet("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetLocationAsync(id, true, ct));

        }).WithName("GetLocation")
          .Produces<LocationModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Location" """;
              generatedOperation.Description = """Get specific "LEXIS Location" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Location Id";
              return generatedOperation;
          });

        group.MapPost("", async ([Validate, FromBody] LocationCreate location, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            var locationId = await service.InsertLocationAsync(location, ct);
            return Results.CreatedAtRoute("GetLocation", new { Id = locationId });

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Create "LEXIS Location" """;
              generatedOperation.Description = """Create specific "LEXIS Location" <br>Locations types: {HPC - 1, Cloud - 2, K8s - 3, Storage - 4, iRODS -5, FirecREST -6, Quantum -7}""";
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapPut("", async ([Validate, FromBody] LocationUpdate location, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            await service.UpdateLocationAsync(location, ct);
            return Results.Ok("LEXIS Location was modified.");

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Update "LEXIS Location" """;
              generatedOperation.Description = """Update specific "LEXIS Location" <br>Locations types: {HPC - 1, Cloud - 2, K8s - 3, Storage - 4, iRODS -5, FirecREST -6, Quantum -7}""";
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapDelete("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            await service.DeleteLocationAsync(id, ct);
            return Results.NoContent();

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status204NoContent)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Remove "LEXIS Location" """;
              generatedOperation.Description = """Remove specific "LEXIS Location" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation?.Parameters[1];
              //parameter2?.Description = "LEXIS Location Id";
              return generatedOperation;
          });
    }
}