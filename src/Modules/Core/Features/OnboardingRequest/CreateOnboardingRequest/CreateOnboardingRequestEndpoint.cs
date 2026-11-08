using Core.Domain.OnboardingRequest;
using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;
using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.OnboardingRequest.CreateOnboardingRequest;

/// <summary>
/// Creates an onboarding request: the caller asks to join the route project as a member (ADR-0048).
/// The onboarding twin of <c>POST requests</c> — the request type is implicitly self-onboarding, the
/// project comes from the route, and it writes the dedicated <c>OnboardingRequest</c> aggregate
/// (ADR-0046). Any authenticated caller may create one (no project-scope check — you have no role on
/// the project you are joining). Notifications are produced inline after the request is saved.
/// </summary>
internal static class CreateOnboardingRequestEndpoint
{
    internal sealed record Request([FromRoute] string ProjectShortName);

    internal sealed record Response(string Slug);

    internal static async Task<IResult> HandleAsync(
        [AsParameters] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IRepository<OnboardingRequestAggregate, OnboardingRequestSlug> onboardingRequests,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IProjectDirectory projectDirectory,
        [FromServices] IOnboardingRequestQuery onboardingRequestQuery,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        CancellationToken ct)
    {
        if (!ProjectShortName.TryParse(request.ProjectShortName, null, out var shortName))
            return Problems.Validation(CommonErrorCodes.Required, "Project short name is required.");

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } requesterUserId)
            return Problems.CallerNotProvisioned();

        var requester = await users.FindAsync(caller.IdpId, ct);
        if (requester is null)
            return Problems.NotFound(CommonErrorCodes.CallerNotProvisioned);

        var project = await projects.FindAsync(shortName, ct);
        if (project is null)
            return Problems.NotFound(OnboardingRequestErrorCodes.NotFound, "Project not found.");

        if (project.Users.Any(u => u.UserName == requester.UserName))
            return Problems.Conflict(OnboardingRequestErrorCodes.AlreadyActiveMember, $"You are already an active member of project '{shortName.Value}'.");

        var projectId = await projectDirectory.FindIdAsync(shortName, ct);
        if (projectId is null)
            return Problems.NotFound(OnboardingRequestErrorCodes.NotFound, "Project not found.");

        if (await onboardingRequestQuery.FindPendingIdAsync(projectId.Value, requesterUserId, ct) is not null)
            return Problems.Conflict(OnboardingRequestErrorCodes.DuplicatePendingRequest, $"You already have a pending request for project '{shortName.Value}'.");

        var slug = await CreateAndNotifyAsync(
            requester.Email, project.Email, caller.IdpId, shortName,
            onboardingRequests, onboardingRequestQuery, notifications, unitOfWork, cache, ct);

        return Results.Created($"v2/Projects/{shortName.Value}/OnboardingRequests/{slug.Value}", new Response(slug.Value));
    }

    private static async Task<OnboardingRequestSlug> CreateAndNotifyAsync(
        Email requesterEmail,
        Email? ownerEmail,
        IdpId requesterIdpId,
        ProjectShortName shortName,
        IRepository<OnboardingRequestAggregate, OnboardingRequestSlug> onboardingRequests,
        IOnboardingRequestQuery onboardingRequestQuery,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        CancellationToken ct)
    {
        // GUID-free public id (ADR-0024). The slug is the aggregate key, generated up-front and passed into
        // Create. ponytail: no regenerate-retry — the r6 suffix makes a same requester+project collision
        // vanishingly rare; a hit rolls back the single UnitOfWork cleanly and the client re-POSTs.
        var slug = OnboardingRequestSlug.New(requesterEmail, shortName);

        var newRequest = OnboardingRequestAggregate.Create(
            shortName,
            requesterIdpId,
            requesterEmail,
            slug);

        await onboardingRequests.AddAsync(newRequest, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Read back the surrogate id (db-generated; not exposed on the aggregate) for the notification emails
        // only — it is an internal concern, never the contract surface (the slug is).
        var requestId = await onboardingRequestQuery.FindIdBySlugAsync(slug.Value, ct) ?? Guid.Empty;
        await notifications.SendRequestCreatedToRequesterAsync(requesterEmail, requestId, shortName, ct);
        if (ownerEmail is { } owner)
            await notifications.SendRequestCreatedToOwnerAsync(owner, requestId, shortName, ct);

        await cache.EvictAsync(CacheTags.Project(shortName.Value), ct);

        return slug;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateOnboardingRequestEndpoint()
        {
            app.MapPost("Projects/{projectShortName}/OnboardingRequests", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateOnboardingRequestV2")
            .WithSummary("Create an onboarding request")
            .WithDescription("Creates an onboarding request to join the route project as a member. The project is taken from the route; the request type is implicitly self-onboarding (no body). Any authenticated caller may create one (no project-scope check). Returns 201 with the GUID-free slug; 404 (onboarding_request.not_found / caller.not_provisioned) when the project or caller is unknown; 409 when already a member (onboarding_request.already_active_member) or a pending request exists (onboarding_request.duplicate_pending_request). On a failure read the 'code' extension; do not parse the message.")
            .WithTags("OnboardingRequest")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();
    }
}