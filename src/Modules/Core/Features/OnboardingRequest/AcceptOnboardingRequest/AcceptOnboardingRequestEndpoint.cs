using Core.Domain.OnboardingRequest;
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

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.OnboardingRequest.AcceptOnboardingRequest;

/// <summary>
/// Accepts a pending onboarding request, granting the requester project membership atomically (ADR-0019).
/// The onboarding twin of <c>PUT Requests/{slug}/Accept</c>. Project scope is enforced on the route project
/// (ADR-0048): the caller must hold a role on it. Because both the project and the slug are in the path, the
/// handler also asserts the resolved request belongs to the route project; a genuine miss, an out-of-scope
/// caller, and a wrong-project slug all return the byte-identical <c>onboarding_request.not_found</c> problem
/// so existence never leaks (ADR-0018 rider).
/// </summary>
internal static class AcceptOnboardingRequestEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName, [FromRoute] string Slug);

    // A genuine miss, an out-of-scope caller, and a wrong-project slug MUST return the identical problem so
    // existence never leaks (ADR-0018 rider; ADR-0048 wrong-project rider). All paths use this code + detail.
    private const string NotFoundCode = OnboardingRequestErrorCodes.NotFound;
    private const string NotFoundDetail = "Request not found.";

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IOnboardingRequestQuery onboardingRequestQuery,
        [FromServices] IRepository<OnboardingRequestAggregate, OnboardingRequestSlug> onboardingRequests,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!ProjectShortName.TryParse(request.ProjectShortName, null, out var routeShortName))
            return Problems.NotFound(NotFoundCode, NotFoundDetail);

        if (await OnboardingSettlementLookup.FindSettleableAsync(
                request.Slug, routeShortName, caller, AuditActions.OnboardingRequestSelfAccepted,
                onboardingRequestQuery, onboardingRequests, auditLogger, ct) is not { } found)
            return Problems.NotFound(NotFoundCode, NotFoundDetail);

        var (settlement, settledRequest) = found;

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } assignedBy)
            return Problems.CallerNotProvisioned();

        var outcome = await OnboardingAccept.ExecuteAsync(
            settledRequest, settlement.Requester, routeShortName, assignedBy,
            projects, users, roleListService, roleOptions.Value, ct);
        if (outcome.Failure is { } failure)
            return failure;

        await CompleteAsync(outcome, request.Slug, routeShortName, onboardingRequestQuery, notifications, unitOfWork, cache, httpContext, ct);

        await auditLogger.AuditAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.OnboardingRequestSelfAccepted, caller, routeShortName,
            new Dictionary<string, object?> { ["requester_idp_id"] = outcome.RequesterIdpId.Value }) with
        {
            TargetType = AuditActions.TargetTypes.OnboardingRequest,
            TargetId = request.Slug,
        });

        return Results.NoContent();
    }

    private static async Task CompleteAsync(
        OnboardingAcceptOutcome outcome,
        string slugValue,
        ProjectShortName routeShortName,
        IOnboardingRequestQuery onboardingRequestQuery,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [outcome.RequesterIdpId.Value]);

        // Read back the surrogate id (db-generated; not on the aggregate) for the notification only.
        var requestId = await onboardingRequestQuery.FindIdBySlugAsync(slugValue, ct) ?? Guid.Empty;
        if (outcome.RequesterEmail is { } requesterEmail)
            await notifications.SendRequestAcceptedToRequesterAsync(requesterEmail, requestId, routeShortName, ct);
        await cache.EvictAsync(CacheTags.Project(routeShortName.Value), ct);
        await cache.EvictAsync(CacheTags.UserInfo(outcome.RequesterIdpId.Value), ct);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapAcceptOnboardingRequestEndpoint()
        {
            app.MapPut("Projects/{projectShortName}/OnboardingRequests/{slug}/Accept", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("AcceptOnboardingRequestV2")
            .WithSummary("Accept an onboarding request")
            .WithDescription("Accepts a pending onboarding request and assigns the requester to the route project as a member, atomically. Caller must hold a role on the route project. Returns 204; 404 (onboarding_request.not_found) when the request is unknown, outside the caller's scope, or belongs to a different project — the three are deliberately indistinguishable; 409 when the request is already settled. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("OnboardingRequest")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}