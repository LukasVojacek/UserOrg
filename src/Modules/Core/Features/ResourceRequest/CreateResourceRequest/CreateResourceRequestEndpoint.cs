using Core.DataAccess.ResourceRequests;
using Core.Domain.Shared;
using Core.Features.ResourceRequest.Flow;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.ResourceRequest.CreateResourceRequest;

/// <summary>
/// Creates a project↔resource assignment request (ADR-0063, route 1): an Owner/Manager of the route project asks
/// to attach a provider's resource to it. Gated by <see cref="CallerScope.RequireProjectScope"/> so an
/// out-of-scope project is indistinguishable from a missing one (ADR-0018). <see cref="Request.ResourceName"/> is
/// free text and need not name an existing <see cref="ProjectResource"/> (DEC-1) — the flow's second gate parks
/// on it if it does not exist yet. The caller supplies <see cref="Request.PrimaryInvestigatorEmail"/> directly
/// (legacy parity). The named provider is resolved within the caller's project scope, so one they cannot reach
/// is reported as missing (ADR-0018); it must have a support e-mail to route a not-yet-existing resource to, or
/// creation is refused up front (DEC-5). The named resource must not already be attached to the project, and must have no other
/// in-flight request for the same (project, provider, resource) key. On success the projection row is inserted
/// first, then the <see cref="AssignResourceFlow"/> is started with the slug as its idempotency key; a start
/// failure rolls the row back so a retry is not blocked by the single-active guard. Returns 202 with the slug and
/// a Location header to the project's request list.
/// </summary>
internal static partial class CreateResourceRequestEndpoint
{
    internal sealed record Request(string ProviderName, string ResourceName, string PrimaryInvestigatorEmail);

    internal sealed record Response(string Slug);

    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string projectShortName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IProviderQuery providers,
        [FromServices] IProjectResourceRequestQuery requests,
        [FromServices] IResourceRequestProjectionStore store,
        [FromServices] IWorkflowClient workflow,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (!ProjectShortName.TryParse(projectShortName, null, out var shortName))
            return Problems.Validation(CommonErrorCodes.Required, "Project short name is required.");

        if (caller.RequireProjectScope(shortName) is { } denied)
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(AuditActions.ResourceRequestSelfCreated, caller, shortName) with
            {
                TargetType = AuditActions.TargetTypes.ResourceRequest,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            }, logger);
            return denied;
        }

        if (ValidateBody(request, out var provider, out var resourceName, out var contactEmail) is { } invalid)
            return invalid;

        var project = await projects.FindAsync(shortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        if (await CheckAssignableAsync(project, shortName, provider, resourceName, caller, providers, requests, ct) is { } refused)
            return refused;

        ProjectResourceRequest BuildRow(string rowSlug)
        {
            var now = DateTimeOffset.UtcNow;
            return new ProjectResourceRequest
            {
                Slug = rowSlug,
                ProjectShortName = shortName.Value,
                ProviderName = provider.Value,
                ResourceName = resourceName.Value,
                RequesterIdpId = caller.IdpId.Value,
                ContactEmail = contactEmail.Value,
                State = ResourceRequestState.PendingPiApproval,
                WorkflowRequestId = Guid.Empty,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }

        ValueTask AuditCreatedAsync(string createdSlug) =>
            auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
                AuditActions.ResourceRequestSelfCreated, caller, shortName,
                new Dictionary<string, object?> { ["provider"] = provider.Value, ["resource"] = resourceName.Value }) with
            {
                TargetType = AuditActions.TargetTypes.ResourceRequest,
                TargetId = createdSlug,
            }, logger);

        return await InsertAndStartAsync(shortName.Value, resourceName.Value, BuildRow, AuditCreatedAsync, requests, store, workflow, logger, ct);
    }

    private static IResult? ValidateBody(Request request, out ProviderName provider, out ResourceName resourceName, out Email contactEmail)
    {
        resourceName = default;
        contactEmail = default;

        if (!ProviderName.TryCreate(request.ProviderName, out provider, out var providerNameError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerNameError);

        if (!ResourceName.TryParse(request.ResourceName, null, out resourceName))
            return Problems.Validation("resource.invalid_name", "Resource name is invalid.");

        // Legacy parity (DEC-1): the requester supplies the PI contact, capped at the legacy column's 100 chars.
        if (string.IsNullOrWhiteSpace(request.PrimaryInvestigatorEmail)
            || request.PrimaryInvestigatorEmail.Length > 100
            || !Email.TryParse(request.PrimaryInvestigatorEmail, null, out contactEmail))
            return Problems.Validation(CommonErrorCodes.InvalidFormat, "A valid primary investigator email (max 100 characters) is required.");

        return null;
    }

    private static async Task<IResult?> CheckAssignableAsync(
        ProjectAggregate project,
        ProjectShortName shortName,
        ProviderName provider,
        ResourceName resourceName,
        Caller caller,
        IProviderQuery providers,
        IProjectResourceRequestQuery requests,
        CancellationToken ct)
    {
        // Resolve through the caller-scoped read (ADR-0018), never the unscoped write port: a private provider the
        // caller cannot reach comes back null, so it is reported exactly like a missing one and this 404 cannot be
        // used to enumerate providers. The scope is always the caller's projects — the guard above already proved
        // the caller is a member of the route project, so there is no unscoped (null) tier to grant here.
        var owningProvider = await providers.GetProviderDetailsAsync(provider, [.. caller.Scope], ct);
        if (owningProvider is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        // DEC-5: a resource that does not exist yet parks on the provider's support e-mail; without one the
        // request could never progress past that gate, so it is refused up front instead.
        if (owningProvider.SupportEmail is null)
            return Problems.Unprocessable(ResourceRequestErrorCodes.ProviderNoSupportEmail, $"Provider '{provider.Value}' has no support email to notify.");

        if (project.Resources.Any(r => r.ProviderName == provider && r.Name == resourceName))
            return Problems.Conflict("resource_request.already_attached", $"Resource '{resourceName.Value}' is already attached to project '{shortName.Value}'.");

        if (await requests.HasActiveRequestAsync(shortName.Value, provider.Value, resourceName.Value, ct))
            return DuplicateActive(resourceName.Value, shortName.Value);

        return null;
    }

    private static async Task<IResult> InsertAndStartAsync(
        string projectShortName,
        string resourceName,
        Func<string, ProjectResourceRequest> buildRow,
        Func<string, ValueTask> auditCreated,
        IProjectResourceRequestQuery requests,
        IResourceRequestProjectionStore store,
        IWorkflowClient workflow,
        ILogger logger,
        CancellationToken ct)
    {
        var baseSlug = ResourceRequestSlug.BaseSlug(projectShortName, resourceName);

        var insert = await InsertWithFreeSlugAsync(requests, store, resourceName, projectShortName, baseSlug, buildRow, ct);
        switch (insert.Status)
        {
            case InsertStatus.SingleActive:
            // Lost the single-active race (filtered unique index) to a concurrent create for the same key.
            return DuplicateActive(resourceName, projectShortName);
            case InsertStatus.SlugExhausted:
            // A slug-PK race survived one recompute+retry — genuinely rare; ask the client to retry.
            return Problems.Conflict("resource_request.slug_conflict", "Could not allocate a unique request identifier; please retry.");
        }

        var slug = insert.Slug!;

        // Row-insert-first: if the engine start fails, roll the row back so the single-active guard does not block
        // an otherwise-identical retry (the idempotency key makes a genuine double-submit a no-op on the engine).
        try
        {
            await workflow.StartAsync<AssignResourceFlow>(
                new AssignResourceFlowInput(slug), new SubmitOptions(IdempotencyKey: slug), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogFlowStartFailed(logger, ex);
            await store.DeleteBySlugAsync(slug, ct);
            return Problems.Upstream("resource_request.flow_start_failed", "Failed to start the assignment workflow. Please retry.");
        }

        await auditCreated(slug);

        return TypedResults.Accepted($"v2/Projects/{projectShortName}/ResourceRequests", new Response(slug));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Starting the assignment workflow for a resource request failed; the request row was rolled back")]
    private static partial void LogFlowStartFailed(ILogger logger, Exception exception);

    private static IResult DuplicateActive(string resourceName, string projectShortName) =>
        Problems.Conflict("resource_request.duplicate_active", $"An active request for resource '{resourceName}' already exists on project '{projectShortName}'.");

    private enum InsertStatus { Success, SingleActive, SlugExhausted }

    private readonly record struct InsertOutcome(InsertStatus Status, string? Slug);

    /// <summary>
    /// Inserts the projection row under the lowest currently-free slug (<see cref="ResourceRequestSlug.NextAvailable"/>).
    /// A slug-PK race loses the chosen slug to a concurrent create; that is recovered by recomputing the free slug
    /// and retrying the insert exactly once. A single-active index violation is a genuine duplicate and is reported,
    /// not retried. A second slug-PK loss yields <see cref="InsertStatus.SlugExhausted"/>.
    /// </summary>
    private static async Task<InsertOutcome> InsertWithFreeSlugAsync(
        IProjectResourceRequestQuery requests,
        IResourceRequestProjectionStore store,
        string resourceName,
        string projectShortName,
        string baseSlug,
        Func<string, ProjectResourceRequest> buildRow,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await requests.ListSlugsByBaseAsync(baseSlug, ct);
            var slug = ResourceRequestSlug.NextAvailable(projectShortName, resourceName, existing);
            try
            {
                await store.InsertAsync(buildRow(slug), ct);
                return new InsertOutcome(InsertStatus.Success, slug);
            }
            catch (ResourceRequestConflictException e) when (e.Conflict == ResourceRequestConflict.SingleActive)
            {
                return new InsertOutcome(InsertStatus.SingleActive, null);
            }
            catch (ResourceRequestConflictException)
            {
                // Slug-PK race. Recompute the free slug and retry once; a second loss gives up (409).
                if (attempt == 1)
                {
                    return new InsertOutcome(InsertStatus.SlugExhausted, null);
                }
            }
        }

        return new InsertOutcome(InsertStatus.SlugExhausted, null);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateResourceRequestEndpoint()
        {
            app.MapPost("Projects/{projectShortName}/ResourceRequests", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateResourceRequestV2")
            .WithSummary("Request that a provider's resource be assigned to a project")
            .WithDescription("Creates a project↔resource assignment request (ADR-0063). The caller must be an Owner or Manager of the route project (plan D10; out-of-scope is reported as 404 project.not_found, ADR-0018). Body {ProviderName, ResourceName, PrimaryInvestigatorEmail} names the resource and the PI to route approval to; the resource need not exist yet under that provider (it parks until created). Must not already be attached to the project (409 resource_request.already_attached), and must have no in-flight request for the same project/provider/resource (409 resource_request.duplicate_active). The provider is resolved within the caller's project scope, so one they cannot reach is reported as 404 provider.not_found, exactly like a missing one (ADR-0018). It must have a support e-mail (required to route requests for resources that do not yet exist; 422 resource_request.provider_no_support_email if missing). Starts the approval workflow asynchronously and returns 202 with the GUID-free slug and a Location header to the project's request list. On failure, read the 'code' extension, not the message. Status: 202 Accepted, 400 Bad Request, 404 project.not_found / provider.not_found, 409 resource_request.already_attached / resource_request.duplicate_active, 422 resource_request.provider_no_support_email, 502 resource_request.flow_start_failed (retryable).")
            .WithTags("ResourceRequest")
            .WithGroupName(Core.Composition.ApiAudience.User)
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}