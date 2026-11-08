using Microsoft.AspNetCore.Mvc;

using UserOrg.Shared.Extensions;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectResource;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectResource;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class ProjectResourceEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("ProjectResource")
            .WithTags("ProjectResourceManagement");

        group.MapGet("", async ([FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromQuery(Name = "LocationTypeId")] int? locationTypeId, [FromQuery(Name = "StartDate")] DateTime? startDate, [FromQuery(Name = "EndDate")] DateTime? endDate, [FromServices] IProjectResourceService service, [FromServices] IUserService userService, HttpContext httpContext, CancellationToken ct) =>
        {
            if (!httpContext.TryGetOpenIdUserInfoModel(out var openIdModel))
            {
                return Results.Problem("Invalid token: failed to retrieve user info", statusCode: StatusCodes.Status401Unauthorized);
            }

            var isProvider = openIdModel.ProviderId is not null;
            httpContext.TryGetIsAdmin(out var isAdmin);
            IList<ProjectResourceModel> foundData = [];

            if (isAdmin || isProvider)
            {
                foundData = await service.GetProjectResourcesAsync(openIdModel.ProviderId, projectShortName, null, locationTypeId, startDate, endDate, ct);
            }
            else
            {
                //TODO: Sub could be name of API key instead of UserId
                foundData = await service.GetProjectResourcesForUserAsync(openIdModel.Sub, projectShortName, null, locationTypeId, startDate, endDate, ct);
            }

            return Results.Ok(foundData);
        }).Produces<IEnumerable<ProjectResourceModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Project Resources" """;
              generatedOperation.Description = """Get list of all "LEXIS Project Resources" <br>Status types: {Pending - 1, Accepted - 2, Declined - 3} <br>Locations types: {HPC - 1, Cloud - 2, K8s - 3, Storage - 4, iRODS -5, FirecREST -6, Quantum -7}""";
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              var parameter2 = generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Project Short Name";
              var parameter3 = generatedOperation.Parameters[2];
              parameter3.Description = "LEXIS Status Id";
              var parameter4 = generatedOperation.Parameters[3];
              parameter4.Description = "LEXIS Location Type Id";
              //var parameter5 = generatedOperation.Parameters[4];
              //parameter5.Description = "LEXIS Project Resource StartDate";

              return generatedOperation;
          });

        group.MapGet("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProjectResourceService service, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Items.TryGetValue("providerId", out var providerIdTxt);

            httpContext.TryGetProviderId(out var providerId);

            return Results.Ok(await service.GetProjectResourceAsync(providerId, id, ct));

        }).WithName("GetProjectResource")
          .Produces<ProjectResourceModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Project Resource" """;
              generatedOperation.Description = """Get specific "LEXIS Project Resource" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Project Resource Id";
              return generatedOperation;
          });

        group.MapPost("", async ([Validate] ProjectResourceCreate projectResource, [FromServices] IProjectResourceService service, HttpContext context, CancellationToken ct) =>
        {
            var resoruce = await service.GetProjectResourceByNameAsync(projectResource.Name, ct);
            if (resoruce is not null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    { "Name",[ "Project Resource with the same name already exists." ] }
                });
            }
            var projectResourceId = await service.InsertProjectResourceAsync(projectResource, context.Request.Host, ct);
            return Results.CreatedAtRoute("GetProjectResource", new { Id = projectResourceId });

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
              generatedOperation.Summary = """Create "LEXIS Project Resource" """;
              generatedOperation.Description = """Create specific "LEXIS Project Resource" """;
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapPut("", async ([Validate] ProjectResourceUpdate projectResource, [FromServices] IProjectResourceService service, CancellationToken ct) =>
        {
            await service.UpdateProjectResourceAsync(projectResource, ct);
            return Results.Ok("Project Resource was modified.");

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
              generatedOperation.Summary = """Update "LEXIS Project Resource" """;
              generatedOperation.Description = """Update specific "LEXIS Project Resource" <br>Status types: {Pending - 1, Accepted - 2, Declined - 3} <br>Locations types: {HPC - 1, Cloud - 2, K8s - 3, Storage - 4, iRODS -5, FirecREST -6, Quantum -7}""";
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapDelete("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IUserService projectService, [FromServices] IProjectResourceService service, HttpContext ctx, CancellationToken ct) =>
        {
            var userId = await ctx.GetUserInternalId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            await service.DeleteProjectResourceAsync(userId.Value, id, ct);
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
              generatedOperation.Summary = """Remove "LEXIS Project Resource" """;
              generatedOperation.Description = """Remove specific "LEXIS Project Resource" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Provider Service Key";
              //var parameter2 = generatedOperation.Parameters[1];
              //parameter2.Description = "LEXIS Project Resource Id";
              return generatedOperation;
          });
    }
}