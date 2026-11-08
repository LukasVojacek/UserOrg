using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Forge.Core.Features.Role.Services;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.Features.Request.AcceptRequest;

/// <summary>
/// Accepts a pending self-onboarding request, granting the requester project membership (reusing the
/// Slice D <c>AssignUserWithRole</c> write path). Mirrors the self-onboarding branch of legacy
/// <c>PUT api/Requests/{RequestId}/Accept</c>. Project scope is enforced in Core (ADR-0009): the caller
/// must hold a role on the request's own project, closing the legacy request-id-only gap.
/// </summary>
internal static class AcceptRequestEndpoint
{
    internal sealed record Request([FromRoute] string Slug);

    // A genuinely-missing request and one outside the caller's scope MUST return the identical problem so
    // existence never leaks (ADR-0018 rider). Both paths use this single code + detail.
    private const string NotFoundDetail = "Request not found.";

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRequestQuery requestQuery,
        [FromServices] IRepository<RequestAggregate, RequestId> requests,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IProjectDirectory projectDirectory,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Address by the GUID-free slug (ADR-0024): resolve to the surrogate id, then load via the keyed
        // repo. A v1-created request (null slug) never resolves here → the same 404 as a genuine miss.
        var requestId = await requestQuery.FindIdBySlugAsync(request.Slug, ct);
        if (requestId is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);          // unknown / v1 slug

        var settledRequest = await requests.FindAsync(RequestId.Create(requestId.Value), ct);
        if (settledRequest is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);          // genuine miss

        if (settledRequest.Type != RequestType.SelfOnboarding)
            return Problems.Unprocessable(RequestErrorCodes.UnsupportedType, "Only self-onboarding requests can be handled on this endpoint.");

        var shortName = await projectDirectory.FindShortNameAsync(settledRequest.ProjectId, ct);
        if (shortName is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);
        if (!caller.Scope.Contains(shortName.Value))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.RequestSelfAccepted, caller, shortName.Value) with
            {
                TargetType = AuditActions.TargetTypes.Request,
                TargetId = request.Slug,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return Problems.NotFound(RequestErrorCodes.NotFound, NotFoundDetail);
        }

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } assignedBy)
            return Problems.CallerNotProvisioned();

        var outcome = await SelfOnboardingAccept.ExecuteAsync(
            settledRequest, shortName.Value, assignedBy,
            projects, users, userDirectory, roleListService, roleOptions.Value, ct);
        if (outcome.Failure is { } failure)
            return failure;

        await CompleteAsync(outcome, requestId.Value, shortName.Value, notifications, unitOfWork, cache, httpContext, ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.RequestSelfAccepted, caller, shortName.Value,
            new Dictionary<string, object?> { ["requester_idp_id"] = outcome.RequesterIdpId.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Request,
            TargetId = request.Slug,
        });

        return Results.NoContent();
    }

    private static async Task CompleteAsync(
        SelfOnboardingAcceptOutcome outcome,
        Guid requestId,
        ProjectShortName shortName,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [outcome.RequesterIdpId.Value]);

        if (outcome.RequesterEmail is { } requesterEmail)
            await notifications.SendRequestAcceptedToRequesterAsync(requesterEmail, requestId, shortName, ct);
        await cache.EvictAsync(CacheTags.Project(shortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(outcome.RequesterIdpId.Value), ct);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAcceptRequestEndpoint()
        {
            app.MapPut("Requests/{slug}/Accept", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AcceptRequestV2")
            .WithSummary("Accept a self-onboarding request")
            .WithDescription("[DEPRECATED — use Projects/{projectShortName}/OnboardingRequests] Accepts a pending self-onboarding request and assigns the requester to the project as a member. Caller must hold a role on the request's project. Returns 204; 404 (request.not_found) when the request is unknown or outside the caller's scope — the two are deliberately indistinguishable; 409 when the request is already settled; 422 for non self-onboarding requests. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("Request")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}