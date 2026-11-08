using Microsoft.AspNetCore.Mvc;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResourceAssignment;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResourceAssignment;

namespace UserOrganizationManagement.API.API;

internal sealed class ProjectResourceAssignmentEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("ProjectResourceAssignment")
            .WithTags("ProjectResourceAssignmentManagement");

        group.MapGet("", async ([FromQuery(Name = "ProjectResourceId")] Guid? projectResourceId, [FromServices] IProjectResourceAssignmentService service, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.TryGetProviderId(out var providerId);
            return Results.Ok(await service.GetProjectResourceAssignmentsAsync(providerId, projectResourceId, ct));

        }).Produces<IEnumerable<ProjectResourceAssignmentModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Project Resources Assignments" """;
              generatedOperation.Description = """Get list of all "LEXIS Project Resources Assignments" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Project Resource Id";
              return generatedOperation;
          });

        group.MapGet("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProjectResourceAssignmentService service, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.TryGetProviderId(out var providerId);
            return Results.Ok(await service.GetProjectResourceAssignmentAsync(providerId, id, ct));

        }).WithName("GetProjectResourceAssignment")
          .Produces<ProjectResourceAssignmentModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Project Resource Assignment" """;
              generatedOperation.Description = """Get specific "LEXIS Project Resource Assignment" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Project Resource Assignment Id";
              return generatedOperation;
          });

        group.MapPost("", async ([Validate] ProjectResourceAssignmentCreate projectResourceAssignment, [FromServices] ILocationService locationService, [FromServices] IProjectResourceAssignmentService service, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.TryGetProviderId(out var providerId);

            if (providerId is not null)
            {
                var locations = await locationService.GetLocationIdsForProviderIdAsync(providerId.Value, ct);

                // Not allowed locations for provider id
                if (!locations.Any(x => x == projectResourceAssignment.LocationId.Value))
                {
                    return Results.BadRequest();
                }
            }

            var projectResourceAssignmentId = await service.InsertProjectResourceAssignmentAsync(projectResourceAssignment, ct);
            return Results.CreatedAtRoute("GetProjectResourceAssignment", new { Id = projectResourceAssignmentId });

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Create "LEXIS Project Resource Assignment" """;
              generatedOperation.Description = """Create specific "LEXIS Project Resource Assignment" <br>Allocation unit types: {Unspecified - 1, Node-hours - 2, Core-hours - 3, Credits - 4, GB - 5, QPU-seconds - 6} """;
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapPut("", async ([Validate] ProjectResourceAssignmentUpdate projectResourceAssignment,
           [FromServices] IProjectResourceAssignmentService service, HttpContext httpContext, CancellationToken ct) =>
        {

            await service.UpdateProjectResourceAssignmentAsync(projectResourceAssignment, ct);
            return Results.Ok("Project Resource Assignment was modified.");

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Update "LEXIS Project Resource Assignment" """;
              generatedOperation.Description = """Update specific "LEXIS Project Resource Assignment" <br>Allocation unit types: {Unspecified - 1, Node-hours - 2, Core-hours - 3, Credits - 4, GB - 5, QPU-seconds - 6} """;
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapDelete("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProjectResourceAssignmentService service, CancellationToken ct) =>
        {
            await service.DeleteProjectResourceAssignmentAsync(id, ct);
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
              generatedOperation.Summary = """Remove "LEXIS Project Resource Assignment" """;
              generatedOperation.Description = """Remove specific "LEXIS Project Resource Assignment" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Project Resource Assignment Id";
              return generatedOperation;
          });
    }
}