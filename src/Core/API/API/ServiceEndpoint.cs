using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Others;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Service;

namespace UserOrganizationManagement.API.API;

internal static class ServiceEndpoint
{
    public static RouteGroupBuilder RegisterServiceEndpoint(this WebApplication app)
    {
        var group = app.MapGroup("api/Service")
          .WithTags("Service");

        group.MapGet("SyncDetails", async (HttpContext context, [FromServices] IAdministrationService service, CancellationToken ct) =>
        {
            if (!context.TryGetProviderId(out var providerId))
            {
                return Results.BadRequest("Provider id not found");
            }

            var results = await service.GetSyncDetails(providerId.GetValueOrDefault(), ct);
            return Results.Ok(results);

        }).WithName("SyncDetails")
          .WithDescription("Get synchronization details for a Provider.")
          .Produces<List<UserInfoExtendedModel>>(StatusCodes.Status200OK)
          .ProducesValidationProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status401Unauthorized)
          .ProducesProblem(StatusCodes.Status403Forbidden)
          .ProducesValidationProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status429TooManyRequests)
          .ProducesProblem(StatusCodes.Status500InternalServerError)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get synchronization details for a "LEXIS Provider" """;
              generatedOperation.Description = """Get synchronization details for a "LEXIS Provider" """;
              //var parameter = generatedOperation.Parameters[0];
              //parameter.Description = "Provider Service Key";
              return generatedOperation;
          });

        group.MapGet("ProjectsInfo", GetProjectsInfo)
             .WithName("ProjectsInfo")
             .WithDescription("Get information about all projects grouped by provider with active resources")
             .Produces<IEnumerable<ProjectInfoModel>>(StatusCodes.Status200OK)
             .ProducesProblem(StatusCodes.Status400BadRequest)
             .ProducesProblem(StatusCodes.Status401Unauthorized)
             .ProducesProblem(StatusCodes.Status403Forbidden)
             .ProducesValidationProblem(StatusCodes.Status404NotFound)
             .ProducesProblem(StatusCodes.Status429TooManyRequests)
             .ProducesProblem(StatusCodes.Status500InternalServerError)
             .WithOpenApiDoc(generatedOperation =>
             {
                 generatedOperation.Summary = """Get information about all "LEXIS Projects" grouped by "LEXIS Provider" """;
                 generatedOperation.Description = """Get information about all "LEXIS Projects" grouped by "LEXIS Provider" """;
                 return generatedOperation;
             });

        return group;
    }

    /// <summary>
    /// This method gets information about all projects grouped by provider.
    /// </summary>
    /// <param name="serviceKey">The service key.</param>
    /// <param name="service">The project resource service.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A list of project information models.</returns>
    private static async Task<IList<ProjectInfoModel>> GetProjectsInfo([FromServices] IProjectResourceService service, CancellationToken ct)
    {
        var data = await service.GetAllProjectResourceWithDetailsAsync(ct);
        return data;
    }

    public static void RegisterVersionEndpoint(this WebApplication app)
    {
        var group = app.MapGroup("api");

        group.MapGet("/version", ([FromServices] IConfiguration configuration) =>
        {
            var version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "0.0.0.1";

            return Results.Content(version, "text/plain");
        })
        .WithName("GetVersion")
        .Produces<string>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Get "LEXIS UserOrg" version""";
            generatedOperation.Description = """ Get "LEXIS UserOrg" version""";
            return generatedOperation;
        });
    }

    public static RouteGroupBuilder RegisterPublicEndpoint(this WebApplication app)
    {
        var group = app.MapGroup("api/Public")
            .WithTags("Public");

        group.MapPost("Handle/{actionLink}", async ([FromRoute] string actionLink, [FromServices] IActionLinkService service, [FromServices] IProjectRequestService requestService, CancellationToken ct) =>
        {
            var result = await service.Handle(actionLink, requestService, ct);

            return result ? Results.Ok("Request was handled") : Results.BadRequest();
        }).Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithOpenApiDoc(generatedOperation =>
        {
            generatedOperation.Summary = """Settle a "LEXIS Project" request from its emailed action link""";
            generatedOperation.Description = """
                Settles a "LEXIS Project" resource-assignment request from one of the two single-use action links generated for it and sent to the person who has to decide. The route is unauthenticated: possession of the link is the credential. Which link is presented decides the outcome — the accept link accepts the request, the reject link declines it with an empty reason. Handling then burns EVERY live action link of that request, the accept and the reject link alike, so neither can be replayed. A link that is unknown, already burned or past its validity window does not resolve and the request fails. Returns 200 with "Request was handled" when the link settled the request, and 400 when the link carried neither action.
                """;
            var parameter = generatedOperation.Parameters[0];
            parameter.Description = "The single-use action link identifier taken from the emailed accept or reject URL.";
            return generatedOperation;
        });

        group.MapGet("RequestInfo/{requestId:guid}", async ([FromRoute] Guid requestId, [FromServices] IProjectRequestService requestService, [FromServices] IOptions<Domain.Configuration.ActionLink> linkOptions, CancellationToken ct) =>
        {
            var result = await requestService.GetPublicRequestInfoAsync(requestId, ct);
            Dictionary<string, string[]?> validationProblems = new() { { "Message", null } };

            if (result is null)
            {
                validationProblems["Message"] = ["Request not found"];
            }

            if (IsNotInPendingState(result))
            {
                validationProblems["Message"] = ["Request is not in pending state"];
            }

            if (IsNotValid(result, linkOptions))
            {
                validationProblems["Message"] = ["Request is not valid"];
            }

            if (InvalidType(result))
            {
                validationProblems["Message"] = ["Invalid request type"];
            }

            return validationProblems["Message"] is not null ? Results.ValidationProblem(validationProblems) : Results.Ok(result);
        }).Produces<PublicRequestModel>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .WithOpenApiDoc(generatedOperation =>
          {
              generatedOperation.Summary = """Get the public summary of a "LEXIS Project" resource-assignment request""";
              generatedOperation.Description = """
                  Returns the unauthenticated summary an action-link landing page needs to present the decision: the "LEXIS Project" short name, the "LEXIS Project Resource" name, the "LEXIS Provider", the requester's user name and e-mail, the request state and validity, and BOTH action links — accept and reject — so the page can offer the two buttons. It is served only while the request is still actionable, and is otherwise refused with a 400 validation problem naming the reason: the request must still be pending (not yet accepted or rejected), it must be of type ProjectResourceAssignment, and its action links must not have expired longer ago than the configured action-link duration. The request must also still have exactly its two live action links.
                  """;
              var parameter = generatedOperation.Parameters[0];
              parameter.Description = """Identifier (GUID) of the "LEXIS Project" request the action links belong to.""";
              return generatedOperation;
          });

        return group;
    }
    private static bool IsNotInPendingState(PublicRequestModel result) => result.RequestState > (int)ERequestState.PendingForHPCProject;
    private static bool IsNotValid(PublicRequestModel result, IOptions<Domain.Configuration.ActionLink> durationConfig) => (DateTime.UtcNow - result.ValidTo) > durationConfig.Value.Duration;
    private static bool InvalidType(PublicRequestModel result) => result.RequestType != (int)ERequestType.ProjectResourceAssignment;
}