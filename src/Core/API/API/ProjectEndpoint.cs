using FluentValidation;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.OpenApi;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.API.Validations.AbstractTypes;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.SystemRoleManagement;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class ProjectEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Project")
            .WithTags("ProjectManagement");

        group.MapGet("{ProjectShortName}/Users", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            var projectUsers = await service.GetProjectUsersWithRoles(projectShortName, ct);
            return Results.Ok(projectUsers);

        }).Produces<IEnumerable<ProjectUserModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Users" assigned to "LEXIS Projects" """;
              generatedOperation.Description = """Get list of all "LEXIS Users" associated with "LEXIS Projects" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Short-Name";
              return generatedOperation;
          });

        group.MapGet("/", async ([FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.TryGetUserIdentityProviderId(out var idpUserId);
            return Results.Ok(await service.GetProjectsAsync(projectShortName, idpUserId, ct));

        }).WithName("GetProject")
          .Produces<List<ProjectModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Project" """;
              generatedOperation.Description = """Get specific "LEXIS Project" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Short-Name";
              return generatedOperation;
          });

        group.MapPost("/", async ([Validate][FromBody] ProjectCreate project, [FromServices] IProjectService service, [FromServices] HybridCache cache, HttpContext httpContext, CancellationToken ct) =>
        {
            var userId = await httpContext.GetUserInternalId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            httpContext.TryGetUserIdentityProviderId(out var idpUserId);

            await service.InsertProjectAsync(project, userId.Value, ct);

            await service.AssignedUserToProjectWithRoleAsync(ProjectShortName.Create(project.ShortName), idpUserId, userId.Value, RoleInfoConstants.ProjectOwnerRoleName, ct);
            PermissionsChangedHeader.Append(httpContext, [idpUserId!]);

            await cache.RemoveByTagAsync($"UserInfoModel_{userId}", ct);

            return Results.CreatedAtRoute("GetProject", new { projectShortName = project.ShortName });

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
            generatedOperation.Summary = """Create "LEXIS Project" """;
            generatedOperation.Description = """Create specific "LEXIS Project" <br>Domain types: {Agricultural and Veterinary Sciences - 1, Engineering and Technology - 2, Natural Sciences - 3, Medical and Health Sciences - 4, Others - 5}""";
            return generatedOperation;
        });
        group.MapPut("/", async ([Validate] ProjectUpdate project, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            await service.UpdateProjectAsync(project, ct);
            return Results.Ok("LEXIS Project was modified.");

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
            generatedOperation.Summary = """Update "LEXIS Project" """;
            generatedOperation.Description = """Update specific "LEXIS Project" <br>Domain types: {Agricultural and Veterinary Sciences - 1, Engineering and Technology - 2, Natural Sciences - 3, Medical and Health Sciences - 4, Others - 5}""";
            return generatedOperation;
        });

        group.MapPut("/{ProjectShortName}/User/{IdpUserId}/Assign/{RoleId}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "IdpUserId")] string idpUserId, [FromRoute(Name = "RoleId")] int roleId, HttpContext context, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            context.TryGetUserIdentityProviderId(out var idpRequesterUserId);
            context.TryGetIsAdmin(out var isAdmin);

            var userProjectRole = await service.GetProjectRole(projectShortName, idpRequesterUserId, ct);
            if (userProjectRole == null)
            {
                throw new UserNotAssociatedWithProjectException();
            }

            await service.EditAssignedUserSystemRole(projectShortName, idpUserId, idpRequesterUserId, roleId, isAdmin, ct);
            PermissionsChangedHeader.Append(context, [idpUserId]);
            return Results.Ok("User role for project was modified.");

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Update assigned "LEXIS User Role" for "LEXIS Project" """;
            generatedOperation.Description = """Update assigned "LEXIS User Role" for "LEXIS Project" """;
            Describe(generatedOperation, "ProjectShortName", "LEXIS Project Short-Name");
            Describe(generatedOperation, "IdpUserId", "LEXIS Identity Provider User Id of the user whose role is being changed");
            Describe(generatedOperation, "RoleId", """LEXIS User Role Id to assign, as listed by GET /Project/{ProjectShortName}/ValidRoles. The caller must already hold a role on the project that outranks the role being assigned.""");
            return generatedOperation;
        });

        group.MapDelete("/{ProjectShortName}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct) =>
        {
            await service.DeleteProjectAsync(projectShortName, ct);
            PermissionsChangedHeader.Append(httpContext, [PermissionsChangedHeader.EvictAll]);
            return Results.NoContent();

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesValidationProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Remove "LEXIS Project" """;
            generatedOperation.Description = """Remove specific "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            return generatedOperation;
        });

        group.MapPut("/{ProjectShortName}/ProjectResource/{ProjectResourceName}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "ProjectResourceName")] string projectResourceName,
            [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct, [FromServices] IHeappeSynchronizationService heappeSynchronizationService) =>
        {
            var inputProviderId = httpContext.TryGetProviderId(out var providerId) ? providerId : null;
            if (httpContext.TryGetIsAdmin(out var isAdmin) && isAdmin)
            {
                inputProviderId = null;
            }

            await service.AssignedProjectResourceToProjectAsync(projectShortName, projectResourceName, inputProviderId, ct);

            await heappeSynchronizationService.TriggerSynchronizationForProjectResource(projectShortName, projectResourceName, ct);

            return Results.Ok("LEXIS ProjectResource was assigned to LEXIS Project.");

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status200OK)
       .ProducesProblem(StatusCodes.Status401Unauthorized)
       .ProducesProblem(StatusCodes.Status403Forbidden)
       .ProducesValidationProblem(StatusCodes.Status404NotFound)
       .ProducesProblem(StatusCodes.Status409Conflict)
       .ProducesProblem(StatusCodes.Status429TooManyRequests)
       .ProducesProblem(StatusCodes.Status500InternalServerError)
       .WithOpenApiDoc(generatedOperation =>
       {
           generatedOperation.Summary = """Assign "LEXIS Project Resource" with "LEXIS Project" """;
           generatedOperation.Description = """Assign "LEXIS Project Resource" with "LEXIS Project" """;
           var parameter = generatedOperation.Parameters[0];
           parameter.Description = "LEXIS Project Short-Name";
           var parameter2 = generatedOperation.Parameters[1];
           parameter2.Description = "LEXIS Project Resource Id";
           return generatedOperation;
       });

        group.MapDelete("/{ProjectShortName}/ProjectResource/{ProjectResourceName}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "ProjectResourceName")] string projectResourceName, [FromServices] IProjectService service, CancellationToken ct, [FromServices] IHeappeSynchronizationService heappeSynchronizationService) =>
        {
            await service.UnAssignedProjectResourceToProjectAsync(projectShortName, projectResourceName, ct);

            await heappeSynchronizationService.TriggerSynchronizationForProjectResource(projectShortName, projectResourceName, ct);

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
            generatedOperation.Summary = """Unassign "LEXIS Project Resource" with "LEXIS Project" """;
            generatedOperation.Description = """Unassign "LEXIS Project Resource" with "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            var parameter2 = generatedOperation.Parameters[1];
            parameter2.Description = "LEXIS Project Resource name";
            return generatedOperation;
        });

        group.MapPut("/{ProjectShortName}/User/{IdpUserId}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "IdpUserId")] string idpUserId, HttpContext httpContext, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            var assignedByUserId = await httpContext.GetUserInternalId();
            if (assignedByUserId is null)
            {
                return Results.Unauthorized();
            }

            await service.AssignedUserToProjectWithRoleAsync(projectShortName, idpUserId, assignedByUserId.Value, RoleInfoConstants.ProjectMemberRoleName, ct);
            PermissionsChangedHeader.Append(httpContext, [idpUserId]);
            return Results.Ok("LEXIS User was assigned to LEXIS Project.");

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesValidationProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Associate "LEXIS User" with "LEXIS Project" """;
            generatedOperation.Description = """Associate "LEXIS User" with "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            var parameter2 = generatedOperation.Parameters[1];
            parameter2.Description = "LEXIS Identity Provider User Id";
            return generatedOperation;
        });

        group.MapDelete("/{ProjectShortName}/User/{IdpUserId}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "IdpUserId")] string idpUserId, HttpContext httpContext, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            var unAssignedByUserId = await httpContext.GetUserInternalId();
            if (unAssignedByUserId is null)
            {
                return Results.Unauthorized();
            }

            await service.UnAssignedUserFromProjectAsync(projectShortName, idpUserId, unAssignedByUserId.Value, ct);
            PermissionsChangedHeader.Append(httpContext, [idpUserId]);
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
            generatedOperation.Summary = """Un-associate "LEXIS User" from "LEXIS Project" """;
            generatedOperation.Description = """Un-associate "LEXIS User" with "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            var parameter2 = generatedOperation.Parameters[1];
            parameter2.Description = "LEXIS Identity Provider User Id";
            return generatedOperation;
        });

        group.MapPut("/{ProjectShortName}/ProjectTag/{ProjectTagId:GUID}", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromRoute(Name = "ProjectTagId")] Guid projectTagId, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            await service.AssignedProjectTagToProjectAsync(projectShortName, projectTagId, ct);
            return Results.Ok("LEXIS ProjectTag was assigned to LEXIS Project.");

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesValidationProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Associate "LEXIS ProjectTag" with "LEXIS Project" """;
            generatedOperation.Description = """Associate "LEXIS ProjectTag" with "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            var parameter2 = generatedOperation.Parameters[1];
            parameter2.Description = "LEXIS ProjectTag Id";
            return generatedOperation;
        });

        group.MapDelete("/{ProjectShortName}/ProjectTag/{ProjectTagId:GUID}", async ([FromRoute] ProjectShortName projectShortName, [FromRoute(Name = "ProjectTagId")] Guid projectTagId, [FromServices] IProjectService service, CancellationToken ct) =>
        {
            await service.UnAssignedProjectTagToProjectAsync(projectShortName, projectTagId, ct);
            return Results.NoContent();

        }).DisabledWhen("ReadOnlyMode")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesValidationProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Un-associate "LEXIS ProjectTag" from "LEXIS Project" """;
            generatedOperation.Description = """Un-associate "LEXIS ProjectTag" with "LEXIS Project" """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "LEXIS Project Short-Name";
            var parameter2 = generatedOperation.Parameters[1];
            parameter2.Description = "LEXIS ProjectTag Id";
            return generatedOperation;
        });

        group.MapGet("/TemplatesInfo/{projectShortName}", async ([FromRoute] ProjectShortName projectShortName, [FromQuery] bool? noCache, [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct) =>
        {
            if (!httpContext.TryGetOriginalAuthToken(out var bearerToken))
                throw new BearerTokenNotFoundException();

            var result = await service.ProjectTemplateInfo(projectShortName, bearerToken, noCache ?? false, ct);

            // Diagnostic header: which HEAppE instances did not return results (provider/resource), so a caller
            // can tell why a cluster appears unavailable. Kept out of the body so the OpenAPI contract is unchanged.
            // TODO (#219): this header is a stopgap because we cannot change the OpenAPI schema / response models
            // right now. In the refactor version, carry the unavailable-instance info INSIDE the response object
            // (e.g. on TemplateInfoResponse or a wrapped envelope) instead of this side-channel header, and drop
            // the X-Unavailable-HEAppE-Instances header.
            if (result.UnavailableInstances.Count > 0)
                httpContext.Response.Headers.Append("X-Unavailable-HEAppE-Instances", ToAsciiHeaderValue(string.Join("; ", result.UnavailableInstances)));

            return result.Responses;
        })
        .WithName("TemplatesInfo")
        // The description lives in the WithOpenApiDoc delegate below, which runs later and would overwrite
        // anything set here; keeping a second copy on WithDescription only invites the two to drift apart.
        .Produces<List<TemplateInfoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesValidationProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get the HEAppE command templates available to a "LEXIS Project" """;
            generatedOperation.Description = """
                Gets the HEAppE command templates available to the "LEXIS Project", collected from every HEAppE instance behind the project's resources using the caller's own bearer token. Results are cached; the response is served from the cache unless noCache is set. HEAppE instances that did not answer are not an error: their templates are simply absent from the body, and the instances are named in the X-Unavailable-HEAppE-Instances response header so a caller can tell why a cluster looks unavailable.
                """;
            Describe(generatedOperation, "projectShortName", "LEXIS Project Short-Name");
            Describe(generatedOperation, "noCache", "Pass true to bypass both cache layers (TemplatesInfo and HEAppE clusters) and query HEAppE directly without storing the result; the existing cached entry is left untouched.");
            return generatedOperation;
        });

        group.MapGet("/ExtendedProjectInfo", async ([FromQuery(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetExtendedProjectInfoAsync(projectShortName, ct));

        }).WithName("ExtendedProjectInfo")
          .Produces<List<ExtendedProjectModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get extended "LEXIS Project" """;
              generatedOperation.Description = """Get extended specific "LEXIS Project" """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Short-Name";
              return generatedOperation;
          });

        group.MapGet("/{ProjectShortName}/ValidRoles", async ([FromRoute(Name = "ProjectShortName")] ProjectShortName projectShortName, [FromServices] IRoleListService roleListService, CancellationToken ct, HttpContext httpContext) =>
        {
            httpContext.TryGetUserIdentityProviderId(out var idpUserId);
            var projectRoles = httpContext.GetUserProjectRoleNames();

            var userRoleNamesForProject = projectRoles
                .Where(kvp => kvp.Value.Any(p => p == projectShortName))
                .Select(kvp => kvp.Key)
                .ToList();

            var rolesRequest = await roleListService.ListRoles(ct);
            if (!rolesRequest.IsSuccess)
            {
                throw new Exception("Failed to retrieve roles from Role Service.");
            }

            var systemRoleModels = ValidRolesFilter
                .GetValidRoles(rolesRequest.Value, userRoleNamesForProject, RoleInfoConstants.ProjectMemberRoleName)
                .Select(role => new SystemRoleModel(
                    role.Name,
                    role.Id,
                    role.Description,
                    role.Permissions.Select(p => p.Key.Value).ToList()))
                .ToList();

            return Results.Ok(systemRoleModels);

        }).WithName("ValidRoles")
          .Produces<List<SystemRoleModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = "Get valid roles for project and current user";
              generatedOperation.Description = "Return a list of system roles that are valid for the specified project and current user to change to.";
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Project short name";
              return generatedOperation;
          });
    }

    /// <summary>
    /// Sets a parameter's description by NAME, and does nothing when the operation has no such parameter.
    /// Positional indexing into <c>Parameters</c> breaks as soon as a route gains or loses a parameter, and
    /// the order of route versus query parameters in the generated operation does not follow the handler's
    /// signature.
    /// </summary>
    private static void Describe(OpenApiOperation operation, string name, string description)
    {
        var parameter = operation.Parameters?.FirstOrDefault(p => p.Name == name);
        if (parameter is not null)
        {
            parameter.Description = description;
        }
    }

    /// <summary>
    /// Folds a diagnostic header value to printable ASCII. Provider/resource/location names come from the
    /// database and may contain characters such as the en dash (U+2013), which Kestrel rejects when writing
    /// the response header - turning an otherwise successful degraded response into a 500.
    /// </summary>
    internal static string ToAsciiHeaderValue(string value) =>
        new string(value.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
}