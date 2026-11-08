using System.Text.Json;

using Core.Features.Project;
using Core.Features.Request.Notifications;
using Core.Features.Resource.CreateResourceOnLocation;

using Heappe.Contracts;

using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;

using WorkflowEngine.Contracts.Exceptions;
using WorkflowEngine.Core.Ports;

using static Core.Shared.Auth.ReadScope;

using ProjectAggregate = Core.Domain.Project.Project;
using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.Features.Resource.PublishResource;

internal static class PublishResourceEndpoint
{
    internal sealed class AuditCategory;

    internal static async Task<IResult> HandleAsync(
        [FromRoute] string providerName,
        [FromRoute] string resourceName,
        Caller caller,
        [FromServices] IRepository<ResourceAggregate, ProjectResourceKey> repository,
        [FromServices] IProjectResourceRequestQuery resourceRequestQuery,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> projects,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        [FromServices] INotificationSender sender,
        [FromServices] IProviderRepository providerRepository,
        [FromServices] IHeappeSynchronizationPort heappeSynchronizationPort,
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

        var resource = await repository.FindAsync(new ProjectResourceKey(provider, name), ct);
        if (resource is null)
            return Problems.NotFound("resource.not_found", "Resource not found.");

        //try to find resource request for this resource, if found assign resource to project
        string projectShortName = string.Empty;
        var resourceRequest = await resourceRequestQuery.FindByResourceKeyAsync(providerName, resourceName, ct);
        if (resourceRequest != null)
        {
            var project = await projects.FindAsync(ProjectShortName.From(resourceRequest.ProjectShortName), ct);
            if (project is null)
            {
                logger.LogWarning("Project for resource request {ResourceRequest} not found.", resourceRequest.Slug);
                return Problems.NotFound("project.not_found", "Project not found.");
            }
            projectShortName = project.ShortName.Value;

            //assign resource to project
            var assignedResult = project.AssignResource(resource);
            if (!assignedResult.IsSuccess)
                return assignedResult.Error.ToProblem();

            //trigger create templates
            await heappeSynchronizationPort.TriggerSynchronizationForProjectResource(project.ShortName.Value, resourceName, ct);
        }
        else
        {
            logger.LogWarning("Resource request for provider {Provider} and resource {Resource} not found.", providerName, resourceName);
        }

        resource.UpdateState(EProjectResourceState.Published);
        await unitOfWork.SaveChangesAsync(ct);

        // Send notification to provider support email if available
        var providerEntity = await providerRepository.FindAsync(provider, ct)
            ?? throw new PermanentException("PROVIDER_NOT_FOUND", $"Provider {provider} was not found.");
        if (providerEntity.SupportEmail.HasValue)
        {
            await sender.SendResourcePublishedAsync(providerEntity.SupportEmail.Value, resourceName, projectShortName, ct);
        }

        // Send notification to provider investigator
        if (resource.PrimaryInvestigatorEmail.HasValue)
        {
            await sender.SendResourcePublishedAsync(resource.PrimaryInvestigatorEmail.Value, resourceName, projectShortName, ct);
        }

        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ResourceSelfPublished, caller, data: new Dictionary<string, object?> { ["provider"] = provider.Value }) with
        {
            TargetType = AuditActions.TargetTypes.Resource,
            TargetId = name.Value,
        }, logger);

        return Results.NoContent();
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapPublishResourceEndpoint()
        {
            app.MapPost("Providers/{providerName}/Resources/{resourceName}/Publish", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation()
                .WithGroupName(Core.Composition.ApiAudience.ProviderAdmin);
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("PublishResource")
            .WithSummary("Publish a provider's resource")
            .WithDescription("Publishes a resource under a provider (Admin or the owning provider identity). Returns 204; 400 on validation; 403 when not allowed; 404 when the resource does not exist under this provider. Use the machine-readable `code`, not the message.")
            .WithTags("Resource")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}