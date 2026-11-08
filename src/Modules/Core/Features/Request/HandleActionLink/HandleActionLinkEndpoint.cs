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

namespace Core.Features.Request.HandleActionLink;

/// <summary>
/// Settles a self-onboarding request through a single-use action-link token, without authentication —
/// the v2 replacement for legacy <c>POST api/Public/Handle/{actionLink}</c>. Possession of the link is
/// the authorization; using one link invalidates all siblings. Accept assigns the requester as a member
/// (stamped as self-initiated, since the anonymous link carries no approver identity).
/// </summary>
internal static class HandleActionLinkEndpoint
{
    internal sealed record Request([FromRoute] string ActionLink);

    internal sealed record Response(string Message);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
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
        var resolution = await ResolveAsync(request.ActionLink, requestQuery, requests, projectDirectory, ct);
        if (resolution.Link is not { } link)
            return await DeniedAsync(resolution.DenialReason);

        var (target, settledRequest, shortName) = link;

        // Single-use: handling through one link invalidates every sibling link of the request.
        settledRequest.InvalidateActionLinks();

        var settled = Settled(link, request.ActionLink);
        return target.Action switch
        {
            ActionLinkAction.Accept => await AcceptAsync(
                settledRequest, target.RequestId, shortName, projects, users, userDirectory, roleListService,
                roleOptions.Value, notifications, unitOfWork, cache, auditLogger, settled, httpContext, ct),
            ActionLinkAction.Reject => await RejectAsync(
                settledRequest, target.RequestId, shortName, notifications, unitOfWork, auditLogger, settled, ct),
            _ => await DeniedAsync(RequestErrorCodes.InvalidActionLink),
        };

        async Task<IResult> DeniedAsync(string reason)
        {
            await auditLogger.AuditAsync(ActionLinkAudit.Denied(AuditActions.RequestActionLinkSettled, request.ActionLink, reason));
            return InvalidActionLink();
        }
    }

    private sealed record ResolvedLink(ActionLinkTarget Target, RequestAggregate Request, ProjectShortName ShortName);

    private sealed record Resolution(ResolvedLink? Link, string DenialReason);

    private static async Task<Resolution> ResolveAsync(
        string actionLink,
        IRequestQuery requestQuery,
        IRepository<RequestAggregate, RequestId> requests,
        IProjectDirectory projectDirectory,
        CancellationToken ct)
    {
        var target = await requestQuery.FindActionLinkTargetAsync(actionLink, ct);
        if (target is null)
            return new(null, RequestErrorCodes.NotFound);
        if (target.ValidTo < DateTimeOffset.UtcNow)
            return new(null, RequestErrorCodes.Expired);

        var settledRequest = await requests.FindAsync(RequestId.Create(target.RequestId), ct);
        if (settledRequest is null)
            return new(null, RequestErrorCodes.NotFound);
        if (settledRequest.Type != RequestType.SelfOnboarding)
            return new(null, RequestErrorCodes.InvalidType);

        var shortName = await projectDirectory.FindShortNameAsync(settledRequest.ProjectId, ct);
        if (shortName is null)
            return new(null, AuditActions.DenialReasons.ProjectNotFound);

        return new(new(target, settledRequest, shortName.Value), string.Empty);
    }

    private static AuditEvent Settled(ResolvedLink link, string actionLink) =>
        ActionLinkAudit.Success(AuditActions.RequestActionLinkSettled, actionLink, link.ShortName.Value) with
        {
            TargetType = AuditActions.TargetTypes.Request,
            TargetId = link.Request.Slug ?? link.Target.RequestId.ToString(),
        };

    private static async Task<IResult> AcceptAsync(
        RequestAggregate settledRequest,
        Guid requestId,
        ProjectShortName shortName,
        IRepository<ProjectAggregate, ProjectShortName> projects,
        IRepository<Domain.User.User, IdpId> users,
        IUserDirectory userDirectory,
        IRoleListService roleListService,
        WellKnownRoles roleOptions,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        IAuditLogger<AuditCategory> auditLogger,
        AuditEvent settled,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var outcome = await SelfOnboardingAccept.ExecuteAsync(
            settledRequest, shortName, settledRequest.UserId,
            projects, users, userDirectory, roleListService, roleOptions, ct);
        if (outcome.Failure is { } failure)
            return failure;

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [outcome.RequesterIdpId.Value]);

        if (outcome.RequesterEmail is { } requesterEmail)
            await notifications.SendRequestAcceptedToRequesterAsync(requesterEmail, requestId, shortName, ct);
        await cache.EvictAsync(CacheTags.Project(shortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(outcome.RequesterIdpId.Value), ct);
        await auditLogger.AuditAsync(settled);

        return Results.Ok(new Response("Request was handled."));
    }

    private static async Task<IResult> RejectAsync(
        RequestAggregate settledRequest,
        Guid requestId,
        ProjectShortName shortName,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IAuditLogger<AuditCategory> auditLogger,
        AuditEvent settled,
        CancellationToken ct)
    {
        var reject = settledRequest.Reject();
        if (!reject.IsSuccess)
            return reject.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);

        if (settledRequest.ContactEmail is { } requesterEmail)
            await notifications.SendRequestRejectedToRequesterAsync(requesterEmail, requestId, shortName, ct);
        await auditLogger.AuditAsync(settled);

        return Results.Ok(new Response("Request was handled."));
    }

    private static IResult InvalidActionLink() =>
        Problems.Validation(RequestErrorCodes.InvalidActionLink, "Action link is invalid, expired, or already handled.");

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapHandleActionLinkEndpoint()
        {
            app.MapPost("Public/ActionLinks/{actionLink}", HandleAsync)
                .AllowAnonymous()
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("HandleActionLinkV2")
            .WithSummary("Handle a request action link")
            .WithDescription("Settles a self-onboarding request via a single-use action-link token. No authentication required. Using a link invalidates its siblings. Returns 200 when handled; 400 (code request.invalid_action_link) for an unknown, expired, or already-handled link. On a failure read the machine-readable code; do not parse the message.")
            .WithTags("Public")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesValidationProblem();
    }
}