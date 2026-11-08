using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using Core.DataAccess.ResourceRequests;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared;
using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Contracts.Ports;
using WorkflowEngine.Core.Ports;

using static Google.Protobuf.Reflection.SourceCodeInfo.Types;

namespace Core.Features.Resource.CheckAccountAccess;

internal static class CheckAccountAccessEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string resourceName,
        Caller caller,
        [FromServices] IRequestRepository requestRepository,
        [FromServices] IProviderRepository providerRepository,
        [FromServices] IRequestApplicationService requestApplicationService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        [FromServices] IHeappeService heappeService,
        [FromServices] IProjectCreationService heappeProjectService,
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

        // Get the HEAppE project IDs for each assigned location and prepare the user SSH keys
        List<CreateResourceOnLocationEndpoint.UserSshKey> userSshKeys = [];
        foreach (var location in createResourceRequest.AssignedLocations)
        {
            var heappeIdentifier = location.RequireIdentifier();

            var serviceConnectionInfo = await heappeProjectService.GetServiceHeappeConnectionInfoAsync(heappeIdentifier, ct);
            if (!serviceConnectionInfo.IsSuccess)
            {
                throw new RetryableException("HEAPPE_FAILED_SERVICE_CONNECTION", $"Failed to get service connection info for HEAppE instance {heappeIdentifier}.");
            }
            var project = await heappeService.GetProjectByResourceName(serviceConnectionInfo.Value, createResourceRequest.AccountString!, ct);
            foreach (var username in createResourceRequest.Usernames ?? Enumerable.Empty<string>())
            {
                userSshKeys.Add(new CreateResourceOnLocationEndpoint.UserSshKey
                (
                    location.LocationName,
                    location.AggregationName ?? string.Empty,
                    project.Value.Id,
                    username,
                    null
                ));
            }
        }

        JsonDocument jsonDocument = JsonDocument.Parse(JsonSerializer.Serialize(userSshKeys));

        // Create FinalizeCreationProjectResourceStep from stored workflow state
        var finalizeStep = new FinalizeCreationProjectResourceStep(
            workflowRequest.RequestId,
            provider,
            resourceName,
            providerEntity.SupportEmail!.Value.ToString(),
            createResourceRequest.AssignedLocations,
            resolvedLocations,
            jsonDocument
            );

        // Submit the next workflow step
        await requestApplicationService.ContinueFromStepAsync(workflowRequest.RequestId, "create-project-resource", finalizeStep, ct: ct);


        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCheckAccountAccessEndpoint()
        {
            app.MapPost("Providers/{providerName}/Resources/{resourceName}/CheckAccountAccess", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CheckAccountAccess")
            .WithSummary("Check account access to a clusters")
            .WithDescription("Checks if the current user has access to a clusters.")
            .WithTags("Resource")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}