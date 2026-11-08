using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

using UserOrg.Shared;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.General.Model;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Location;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.ProjectTag;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Provider;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class AdministrationEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Administration")
            .WithTags("AdministrationManagement");

        group.MapGet("Project", async ([FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromQuery(Name = "DomainId")] int? domainId, [FromQuery(Name = "StartDate")] DateTime? startDate, [FromQuery(Name = "EndDate")] DateTime? endDate, [FromServices] IProjectService service, HttpContext httpContext, CancellationToken ct) =>
        {
            var caller = httpContext.GetCallerInfo();

            // Platform admin FIRST, own provider second: a platform admin carries no provider_id claim, so
            // scoping by the claim first would refuse them (ADR-0008 par. 3). A caller with neither is refused.
            Guid? providerScope;
            string? callerIdpUserId;
            if (caller.IsPlatformAdmin)
            {
                providerScope = null;
                callerIdpUserId = null;
            }
            else if (caller.ProviderId is Guid callerProviderId)
            {
                providerScope = callerProviderId;
                callerIdpUserId = caller.IdentityProviderUserId;
            }
            else
            {
                return Results.Forbid();
            }

            return Results.Ok(await service.GetProjectsAsync(projectShortName, null, domainId, startDate, endDate, providerScope, ct, callerIdpUserId));

        }).Produces<List<ProjectModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithMetadata(new ApiExplorerSettingsAttribute { GroupName = "Administration" })
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Projects" """;
              generatedOperation.Description = """Get list of all "LEXIS Projects" <br>Domain types: {Agricultural and Veterinary Sciences - 1, Engineering and Technology - 2, Natural Sciences - 3, Medical and Health Sciences - 4, Others - 5} """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Short-Name";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Domain Id";
              OpenApiParameter parameter3 = (OpenApiParameter)generatedOperation.Parameters[2];
              parameter3.Description = "Start Date";
              OpenApiParameter parameter4 = (OpenApiParameter)generatedOperation.Parameters[3];
              parameter4.Description = "End Date";
              return generatedOperation;
          });

        group.MapGet("ProjectTag", async ([FromServices] IProjectTagService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetProjectTagsAsync(null, ct));
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
              return generatedOperation;
          });

        group.MapGet("User", async ([FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromQuery(Name = "IdpUserId")] string? idpUserId, [FromServices] IUserService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetUsersAsync(projectShortName, idpUserId, ct));
        }).Produces<IEnumerable<UserModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get list of "LEXIS Users" """;
              generatedOperation.Description = """Get list of all "LEXIS Users" """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Project Short-Name";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Identity Provider User Id";
              return generatedOperation;
          });

        group.MapGet("Location", async ([FromQuery(Name = "ProviderName")] string? providerName, [FromQuery(Name = "LocationTypeId")] int? locationTypeId, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetLocationsAsync(providerName, locationTypeId, false, ct));

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
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Provider Name";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Location Type Id";
              return generatedOperation;
          });

        group.MapGet("Location/{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] ILocationService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetLocationAsync(id, false, ct));

        }).Produces<ProviderModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Location" """;
              generatedOperation.Description = """Get specific "LEXIS Location" """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Location Id";
              return generatedOperation;
          });

        group.MapGet("Provider", async ([FromQuery(Name = "CountryCode")] string? countryCode, [FromServices] IProviderService service, [FromServices] IValidator<CountryRequestModel> validator, CancellationToken ct) =>
        {
            var model = new CountryRequestModel(countryCode);
            validator.ValidateAndThrow(model);

            return Results.Ok(await service.GetProvidersAsync(countryCode, false, ct));

        }).Produces<IEnumerable<ProviderModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Lists "LEXIS Providers" """;
              generatedOperation.Description = """Lists all "LEXIS Providers" """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "CountryCode";
              return generatedOperation;
          });

        group.MapGet("Provider/{Id:GUID}", async ([FromRoute(Name = "Id")] Guid id, [FromServices] IProviderService service, CancellationToken ct) =>
        {
            return Results.Ok(await service.GetProviderAsync(id, false, ct));

        }).Produces<ProviderModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Provider" """;
              generatedOperation.Description = """Get specific "LEXIS Provider" """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Provider Id";
              return generatedOperation;
          });

        group.MapPut("PromoteToProviderAdmin", async ([FromQuery(Name = "IdpUserId")] string idpUserId, [FromQuery(Name = "ProviderId")] Guid providerId, [FromServices] IAdministrationService administrationService, CancellationToken ct) =>
        {
            await administrationService.PromoteToProviderAdmin(idpUserId, providerId, ct);
            return Results.Ok("User was associated with Provider Admin role.");

        }).Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Promote "LEXIS User" to "Provider Admin" LEXIS role """;
              generatedOperation.Description = """Promote "LEXIS User" to "Provider Admin" LEXIS role """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Identity Provider User Id";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "LEXIS Provider Id";
              return generatedOperation;
          });

        group.MapPut("ResetToPlatformUser", async ([FromQuery(Name = "IdpUserId")] string idpUserId, [FromServices] IAdministrationService administrationService, CancellationToken ct) =>
        {
            await administrationService.ResetToPlatformUser(idpUserId, ct);
            return Results.Ok("User was reset into Platform User role.");

        }).Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Reset "Provider Admin" role into "Platform User" role.""";
              generatedOperation.Description = """Reset "Provider Admin" role into "Platform User". <br>Does not change roles designed for Projects!!!""";
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "LEXIS Identity Provider User Id";
              return generatedOperation;
          });

        group.MapGet("Requests", async ([FromQuery(Name = "State")] ERequestState? state, [FromQuery(Name = "Type")] ERequestType? requestType, [FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.NotFound(idpUserId);
            }

            List<RequestModel> results = await service.GetRequestsAsync(null, state, projectShortName, requestType, ct);
            return Results.Ok(results);

        }).Produces<List<RequestModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status204NoContent)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Project" requests for "LEXIS Project" that actual "LEXIS User" is responsible for.""";
              generatedOperation.Description = """
                      Get "LEXIS Project" requests for "LEXIS Project" that actual "LEXIS User" is responsible for. <br/><b>States: {Pending -1, Accept - 2, Reject - 3}</b><br/><b>Types: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}</b> 
                      """;
              OpenApiParameter parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "Request State: {Pending -1, Accept - 2, Reject - 3}";
              OpenApiParameter parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "Request Type: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}";

              OpenApiParameter parameter3 = (OpenApiParameter)generatedOperation.Parameters[2];
              parameter3.Description = "LEXIS Project Short-Name";
              return generatedOperation;
          });

        group.MapDelete("Cache", async ([FromServices] IAdministrationService administrationService, CancellationToken ct) =>
        {
            await administrationService.ResetAllCachesAsync(ct);
            return Results.Ok("All cache records were reset.");

        }).Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithSummary("Reset all cache records")
          .WithDescription("Clears every HybridCache entry held by the Core API process. Caches outside HybridCache, such as the OpenID Connect metadata and signing keys held by the authentication middleware, are NOT affected. Neither is the Gateway, which runs as a separate process with its own cache - its JWKS, internal token, Vault access key and user permission caches survive this call.");
    }
}