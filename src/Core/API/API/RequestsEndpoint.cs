using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using UserOrganizationManagement.API.API.AbstractTypes;
using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.InputModels.Project;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.API.API;

internal sealed class RequestsEndpoint : IApiRoute
{
    public void Register(RouteGroupBuilder group)
    {
        group = group.MapGroup("Requests")
                        .WithTags("RequestsManagement");

        group.MapGet("/", async ([FromQuery(Name = "State")] ERequestState? state, [FromQuery(Name = "Type")] ERequestType? requestType, [FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.NotFound(idpUserId);
            }

            var results = await service.GetRequestsAsync(idpUserId, state, projectShortName, requestType, ct);
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
              Get "LEXIS Project" requests for "LEXIS Project" that actual "LEXIS User" is responsible for. <br/><b>States: { Pending - 1, PendingForPIAccept - 2, PendingForHPCProject - 3, Accepted - 4, Rejected - 5 }</b><br/><b>Types: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}</b><br/><b>Note: For SelfOnboarding requests the valid states are: Pending (1), Accepted (4), Rejected (5).</b>
              """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "Request State: {Pending - 1, PendingForPIAccept - 2, PendingForHPCProject - 3, Accepted - 4, Rejected - 5 }";
              var parameter2 = generatedOperation.Parameters[1];
              parameter2.Description = "Request Type: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}";

              var parameter3 = generatedOperation.Parameters[2];
              parameter3.Description = "LEXIS Project Short-Name";
              return generatedOperation;
          });

        group.MapGet("SentRequests", async ([FromQuery(Name = "State")] ERequestState? state, [FromQuery(Name = "Type")] ERequestType? requestType, [FromQuery(Name = "ProjectShortName")] ProjectShortName? projectShortName, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.NotFound(idpUserId);
            }

            var results = await service.GetSentRequestsAsync(idpUserId, state, projectShortName, requestType, ct);
            return Results.Ok(results);

        }).Produces<List<RequestModel>>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status204NoContent)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get "LEXIS Project" sent requests for actual "LEXIS User".""";
              generatedOperation.Description = """
              Get "LEXIS Project" sent requests for actual "LEXIS User". <br/><b>States: { Pending - 1, PendingForPIAccept - 2, PendingForHPCProject - 3, Accepted - 4, Rejected - 5 }</b><br/><b>Types: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}</b><br/><b>Note: For SelfOnboarding requests the valid states are: Pending (1), Accepted (4), Rejected (5).</b>
              """;
              var parameter = (OpenApiParameter)generatedOperation.Parameters[0];
              parameter.Description = "Request State: {Pending - 1, PendingForPIAccept - 2, PendingForHPCProject - 3, Accepted - 4, Rejected - 5 }";
              var parameter2 = (OpenApiParameter)generatedOperation.Parameters[1];
              parameter2.Description = "Request Type: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}";

              var parameter3 = (OpenApiParameter)generatedOperation.Parameters[2];
              parameter3.Description = "LEXIS Project Short-Name";

              parameter.Required = false;
              parameter2.Required = false;
              parameter3.Required = false;
              return generatedOperation;
          });

        group.MapPost("/", async ([FromBody] RequestCreate request, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {
            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            if (!context.TryGetUserIdentityProviderId(out var idpUserId))
            {
                return Results.NotFound(idpUserId);
            }

            var projectShortName = ProjectShortName.Create(request.ProjectShortName);
            await service.AddAssignRequestAsync(projectShortName, idpUserId, request.RequestType, request.ProjectResourceName, request.ProviderId, request.ContactEmail, context.Request.Host, ct)
                ;
            return Results.Ok("Request was created.");

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Create request to assign actual "LEXIS User" to "LEXIS Project""";
              generatedOperation.Description = """Create request to assign actual "LEXIS User" to "LEXIS Project"  <br/><b>States: { Pending - 1, PendingForPIAccept - 2, PendingForHPCProject - 3, Accepted - 4, Rejected - 5 }</b><br/><b>Types: { SelfOnboarding - 1, ProjectResourceAssignment - 2, HEAppECreation - 3}</b><br/><b>Note: For SelfOnboarding requests the valid states are: Pending (1), Accepted (4), Rejected (5).</b>  """;

              return generatedOperation;
          });

        group.MapPut("/{RequestId}/Accept", async ([FromRoute(Name = "RequestId")] Guid requestId, [FromServices] IRequestRepository requestRepo, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {

            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            var userId = await context.GetUserInternalId();
            if (userId is null)
            {
                return Results.NotFound("User Id not found.");
            }

            var request = await requestRepo.GetByIdAsync(requestId, ct);
            if (request.State is ERequestState.Accepted or ERequestState.Rejected)
            {
                return Results.BadRequest("Request was already handled.");
            }

            RequestException.ThrowIfNull(request);
            RequestStateIsNotPendingException.ThrowIfNotPending(request);

            var resultState = await service.AcceptAssignRequestAsync(requestId, userId.Value, ct);

            return (request.Type, resultState) switch
            {
                (ERequestType.SelfOnboarding, _) => Results.Ok("SelfOnboarding Request was accepted."),
                (ERequestType.ProjectResourceAssignment, ERequestState.Accepted) => Results.Ok("ProjectResourceAssignment Request was accepted."),
                (ERequestType.ProjectResourceAssignment, ERequestState.PendingForHPCProject) => Results.Ok("ProjectResourceAssignment Request was accepted and waiting for HPC side."),
                _ => Results.Problem($"{nameof(request.Type)} is not supported")
            };

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
              generatedOperation.Summary = """Accepts request with RequestId""";
              generatedOperation.Description = """Accepts request with RequestId""";
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "OnBoarding Request Id";
              return generatedOperation;
          });

        group.MapPut("/{requestId}/Reject", async ([FromRoute(Name = "requestId")] Guid requestId, [FromQuery(Name = "Reason")] string? reason, [FromServices] IProjectRequestService service, HttpContext context, CancellationToken ct) =>
        {

            if (!context.TryGetAuthToken(out _))
            {
                return TypedResults.Unauthorized();
            }

            await service.DeclineAssignRequestAsync(requestId, reason, ct);
            return Results.Ok("Request was rejected.");

        }).DisabledWhen("ReadOnlyMode")
          .Produces(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Rejects request with requestId with optional Reason message""";
              generatedOperation.Description = """Rejects request with requestId with optional Reason message""";
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = "OnBoarding Request Id";
              var parameter2 = generatedOperation.Parameters[1];
              parameter2.Description = "Reason";
              return generatedOperation;
          });
    }
}