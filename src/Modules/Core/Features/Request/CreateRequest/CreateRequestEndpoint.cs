using Core.Features.Request.Notifications;
using Core.Shared.OutputCache;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

using ProjectAggregate = Core.Domain.Project.Project;
using RequestAggregate = Core.Domain.Request.Request;

namespace Core.Features.Request.CreateRequest;

/// <summary>
/// Creates a self-onboarding request: the caller asks to join a project as a member. Mirrors the
/// self-onboarding branch of legacy <c>POST api/Requests</c>. Per ADR-0009 only <c>SelfOnboarding</c> is
/// accepted here; resource-assignment requests stay on legacy. Action links and notifications are
/// produced inline after the request is saved.
/// </summary>
internal static class CreateRequestEndpoint
{
    internal sealed record Request(string ProjectShortName, string RequestType);

    internal sealed record Response(string Slug);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IRepository<RequestAggregate, RequestId> requests,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IProjectDirectory projectDirectory,
        [FromServices] IRequestQuery requestQuery,
        [FromServices] IActionLinkGenerator actionLinks,
        [FromServices] INotificationSender notifications,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (ValidateBody(request, out var shortName) is { } invalid)
            return invalid;

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } requesterUserId)
            return Problems.CallerNotProvisioned();

        var requester = await users.FindAsync(caller.IdpId, ct);
        if (requester is null)
            return Problems.NotFound(CommonErrorCodes.CallerNotProvisioned);

        var project = await projects.FindAsync(shortName, ct);
        if (project is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, "Project not found.");

        if (project.Users.Any(u => u.UserName == requester.UserName))
            return Problems.Conflict(RequestErrorCodes.AlreadyActiveMember, $"You are already an active member of project '{shortName.Value}'.");

        var projectId = await projectDirectory.FindIdAsync(shortName, ct);
        if (projectId is null)
            return Problems.NotFound(RequestErrorCodes.NotFound, "Project not found.");

        if (await requestQuery.FindPendingSelfOnboardingIdAsync(projectId.Value, requesterUserId, ct) is not null)
            return Problems.Conflict(RequestErrorCodes.DuplicatePendingRequest, $"You already have a pending request for project '{shortName.Value}'.");

        var slug = await CreateAndNotifyAsync(
            requester.Email, project.Email, projectId.Value, requesterUserId, shortName,
            requests, requestQuery, actionLinks, notifications, unitOfWork, cache, ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(AuditActions.RequestSelfCreated, caller, shortName) with
        {
            TargetType = AuditActions.TargetTypes.Request,
            TargetId = slug,
        }, logger);

        return Results.Created($"v2/Requests/{slug}", new Response(slug));
    }

    private static IResult? ValidateBody(Request request, out ProjectShortName shortName)
    {
        shortName = default;

        if (!RequestType.TryParseName(request.RequestType, out var requestType))
            return Problems.Validation(RequestErrorCodes.InvalidType, "Request type must be a valid request type name.");

        if (requestType != RequestType.SelfOnboarding)
            return Problems.Unprocessable(RequestErrorCodes.UnsupportedType, "Only self-onboarding requests are accepted on this endpoint.");

        return ProjectShortName.TryParse(request.ProjectShortName, null, out shortName)
            ? null
            : Problems.Validation(CommonErrorCodes.Required, "Project short name is required.");
    }

    private static async Task<string> CreateAndNotifyAsync(
        Email requesterEmail,
        Email? ownerEmail,
        ProjectId projectId,
        UserId requesterUserId,
        ProjectShortName shortName,
        IRepository<RequestAggregate, RequestId> requests,
        IRequestQuery requestQuery,
        IActionLinkGenerator actionLinks,
        INotificationSender notifications,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        CancellationToken ct)
    {
        // GUID-free public id (ADR-0024). ponytail: no regenerate-retry — the r6 suffix gives a global
        // collision chance ~1/2e9 per same requester+project; a hit throws a clean-rollback 500 (the whole
        // create is one UnitOfWork, nothing commits) and the client re-POSTs to a fresh slug. Add a retry
        // loop only if a collision is ever actually observed.
        var slug = Domain.Request.RequestSlug.New(requesterEmail, shortName);

        var newRequest = RequestAggregate.Create(
            requesterEmail,
            projectId,
            requesterUserId,
            RequestType.SelfOnboarding,
            slug: slug);

        actionLinks.Generate(newRequest);

        await requests.AddAsync(newRequest, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // Read back the surrogate id (db-generated; not exposed on the aggregate) for the notification emails
        // only — it is an internal concern, never the contract surface (the slug is).
        var requestId = await requestQuery.FindPendingSelfOnboardingIdAsync(projectId, requesterUserId, ct) ?? Guid.Empty;
        await notifications.SendRequestCreatedToRequesterAsync(requesterEmail, requestId, shortName, ct);
        if (ownerEmail is { } owner)
            await notifications.SendRequestCreatedToOwnerAsync(owner, requestId, shortName, ct);

        await cache.EvictAsync(CacheTags.Project(shortName.Value), ct);

        return slug;
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateRequestEndpoint()
        {
            app.MapPost("Requests", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateRequestV2")
            .WithSummary("Create a self-onboarding request")
            .WithDescription("[DEPRECATED — use Projects/{projectShortName}/OnboardingRequests] Creates a self-onboarding request to join a project as a member. Body {ProjectShortName, RequestType}. RequestType is the stable PascalCase name; only \"SelfOnboarding\" is accepted (422 with code request.unsupported_type otherwise, 400 for an unknown name). Returns 201; 404 (request.not_found / caller.not_provisioned) when the project or caller is unknown; 409 when already a member or a pending request exists. On a failure read the 'code' extension; do not parse the message.")
            .WithTags("Request")
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesValidationProblem();
    }
}