using System.Text.Json;

using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Steps;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Ports;

namespace Core.Features.Resource.ReprovisionAccounts;

internal static class ReprovisionAccountsEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record ReprovisionAccountsRequest(List<string> Usernames);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string resourceName,
        [FromBody] ReprovisionAccountsRequest request,
        Caller caller,
        [FromServices] IRequestRepository requestRepository,
        [FromServices] IProviderRepository providerRepository,
        [FromServices] IRequestApplicationService requestApplicationService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {

        if (!Core.Domain.Provider.ProviderName.TryCreate(providerName, out var provider, out var providerError))
            return Problems.Validation(ProviderErrorCodes.InvalidName, providerError);

        if (!Core.Domain.Provider.ResourceName.TryParse(resourceName, null, out var name))
            return Problems.Validation("resource.invalid_name", "Resource name is invalid.");

        if (!ProviderWriteAuthorization.Authorize(caller, provider, roleOptions.Value))
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ResourceSelfPublished, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
            {
                TargetType = AuditActions.TargetTypes.Resource,
                TargetId = name.Value,
                Reason = AuditActions.DenialReasons.ProviderWriteForbidden,
            }, logger);
            return Problems.ProviderWriteForbidden();
        }

        if (request.Usernames is null || request.Usernames.Count == 0)
            return Problems.Validation("reprovision.usernames_required", "Usernames are required.");

        var providerEntity = await providerRepository.FindAsync(provider, ct);
        if (providerEntity is null)
            return Problems.NotFound("provider.not_found", "Provider not found.");

        // Load the pending workflow request from storage
        var workflowRequest = await requestRepository.FindByProviderResourceNameAsync(providerName, resourceName, ct);
        if (workflowRequest is null)
            return Problems.NotFound("resource.workflow_not_found", "No pending workflow found for this resource.");

        var dataElement = workflowRequest.Payload.RootElement.GetProperty("Data");
        var createResourceRequest = JsonSerializer.Deserialize<CreateResourceOnLocationEndpoint.Request>(dataElement.GetRawText());
        if (createResourceRequest is null)
            return Problems.Unprocessable("resource.workflow_invalid", "The workflow request payload is invalid.");

        CreateResourceOnLocationEndpoint.ParseAssignments(createResourceRequest.AssignedLocations, out var locationAssignments);
        CreateResourceOnLocationEndpoint.ResolveLocations(providerEntity, provider, createResourceRequest, locationAssignments, out var resolvedLocations);

        // Create the next step in the workflow to initialize the HEAPPE accounts
        var initializeHeappeStep = new InitializeHeappeStep(
            workflowRequest.RequestId,
            provider,
            createResourceRequest,
            resolvedLocations
        );
        // Submit the next workflow step
        //await requestApplicationService.ContinueFromStepAsync(workflowRequest.RequestId, "create-project-resource", initializeHeappeStep, ct: ct);
        await requestApplicationService.RetryStepAsync(workflowRequest.RequestId, "InitializeHeappe", ct);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapProvisionAccountsEndpoint()
        {
            app.MapPost("Providers/{providerName}/Resources/{resourceName}/ProvisionAccounts", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("ProvisionAccounts")
            .WithSummary("Provision accounts for a provider's resource")
            .WithDescription("Provisions accounts for a resource under a provider (Admin or the owning provider identity). Returns 204; 400 on validation; 403 when not allowed; 404 when the resource does not exist under this provider. Use the machine-readable `code`, not the message.")
            .WithTags("Resource")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}