using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectTag;

namespace UserOrganizationManagement.API.API;

internal sealed class ProjectTagEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("ProjectTag")
            .WithTags("ProjectTagManagement");

        group.MapGet("", async ([FromQuery(Name = "TagName")] string? tagName, [FromServices] IProjectTagService service, [FromServices] IValidator<TagRequestModel> validator, CancellationToken ct) =>
        {
            var model = new TagRequestModel(tagName);
            validator.ValidateAndThrow(model);

            return Results.Ok(await service.GetProjectTagsAsync(tagName, ct));

        }).Produces<IEnumerable<ProjectTagModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Project Tags" """;
              generatedOperation.Description = """Get list all of "LEXIS Project Tags" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Tag Name";
              return generatedOperation;
          });

        group.MapGet("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProjectTagService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetProjectTagAsync(id, ct));

        }).WithName("GetProjectTag")
          .Produces<ProjectTagModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
           {
               generatedOperation.Summary = """Get "LEXIS Project Tag" """;
               generatedOperation.Description = """Get specific "LEXIS Project Tag" """;
               var parameter = generatedOperation.Parameters[0];
               parameter.Description = "LEXIS Project Tag";
               return generatedOperation;
           });

        group.MapPost("", async ([Validate] ProjectTagCreate projectTag, HttpContext httpContext, [FromServices] IProjectTagService service, CancellationToken ct) =>
        {
            var userId = await httpContext.GetUserInternalId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var projectTagId = await service.InsertProjectTagAsync(projectTag, userId.Value, ct);
            return Results.CreatedAtRoute("GetProjectTag", new { Id = projectTagId });

        }).Produces(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Create "LEXIS Project Tag" """;
              generatedOperation.Description = """Create specific "LEXIS Project tag" """;
              return generatedOperation;
          });

        group.MapPut("", async ([Validate] ProjectTagUpdate projectTag, [FromServices] IProjectTagService service, CancellationToken ct) =>
        {
            await service.UpdateProjectTagAsync(projectTag, ct);
            return Results.Ok("Project tag was modified.");

        }).Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Update "LEXIS Project Tag" """;
              generatedOperation.Description = """Update specific "LEXIS Project Tag" """;
              return generatedOperation;
          });

        group.MapDelete("{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProjectTagService service, CancellationToken ct) =>
        {
            await service.DeleteProjectTagAsync(id, ct);
            return Results.NoContent();

        }).Produces(StatusCodes.Status204NoContent)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Remove "LEXIS Project Tag" """;
              generatedOperation.Description = """Remove specific "LEXIS Project Tag" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Tag";
              return generatedOperation;
          });
    }
}