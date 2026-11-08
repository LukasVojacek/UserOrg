using System.Diagnostics;

using Heappe.Contracts;
using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CreateProjectCommandTemplate.Projections;
using Heappe.Persistence;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Heappe.Features.CreateProjectCommandTemplate;

internal sealed class ProjectCommandTemplateCreateService(
    ILogger<ProjectCommandTemplateCreateService> logger,
    IHeappeService heappeService,
    HeappeIntegrationDbContext dbContext) : IProjectCommandTemplateCreateService
{
    /// <summary>
    /// Updates partition state to Pending when template creation fails on Heappe side.
    /// </summary>
    private async Task<Result<Error>> UpdatePartitionStateOnFailureAsync(
        ProjectCommandTemplate projectTemplate,
        string queueName,
        string? aggregationName,
        Guid locationId,
        CancellationToken ct)
    {
        var partition = projectTemplate.ProjectCommandTemplatePartitions.FirstOrDefault(f =>
            f.LocationPartition.Name == queueName
            && f.LocationPartition.AggregationName == aggregationName
            && f.LocationPartition.LocationId == locationId);

        if (partition is null)
        {
            logger.LogLocationPartitionRetrievalFailure(projectTemplate.Name, queueName, locationId);

            var locationPartition = await GetLocationPartitionAsync(queueName, aggregationName, locationId, ct);
            var projectCommandTemplatePartition = new ProjectCommandTemplatePartition()
            {
                CommandTemplateId = default,
                State = ECommandTemplateState.Pending,
                LocationPartition = locationPartition,
            };

            dbContext.Set<ProjectCommandTemplatePartition>().Add(projectCommandTemplatePartition);
            dbContext.Set<ProjectCommandTemplate>().Update(projectTemplate);

            var changes = await dbContext.SaveChangesAsync(ct);
            if (changes == 0)
            {
                logger.LogCommandTemplateStateUpdateFailure(projectTemplate.Name, default, queueName, locationId);
                return Result<Error>.Failure(Errors.FailedToUpdateCommandTemplateState(projectTemplate.Name, projectTemplate.Type, default, projectTemplate.ProjectId, projectTemplate.ProjectResourceId), State.Error);
            }
        }

        // Set state to Pending since Heappe creation failed
        if (partition.State != ECommandTemplateState.Pending)
        {
            partition.State = ECommandTemplateState.Pending;
            partition.CommandTemplateId = default;

            var changes = await dbContext.SaveChangesAsync(ct);
            if (changes == 0)
            {
                logger.LogCommandTemplateStateUpdateFailure(projectTemplate.Name, default, queueName, locationId);
                return Result<Error>.Failure(Errors.FailedToUpdateCommandTemplateState(projectTemplate.Name, projectTemplate.Type, default, projectTemplate.ProjectId, projectTemplate.ProjectResourceId), State.Error);
            }
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Creates a new project command template partition associated with the given location and queue.
    /// </summary>
    private async Task<Result<ProjectCommandTemplate, Error>> CreateProjectCommandTemplate(
        string templateName,
        ECommandTemplateType templateType,
        string queueName,
        string? aggregationName,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        Result<CommandTemplateCompactModel, Error> heappeCommandTemplate,
        ProjectCommandTemplate? projectCmdTemplate,
        Guid? commandTemplateDefinitionId,
        ECommandTemplateState defaultTemplateState,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateName, nameof(templateName));
        ArgumentNullException.ThrowIfNull(queueName, nameof(queueName));

        var locationPartition = await GetLocationPartitionAsync(queueName, aggregationName, locationId, ct);
        if (locationPartition is null)
        {
            logger.LogLocationPartitionRetrievalFailure(templateName, queueName, locationId);
            return Result<ProjectCommandTemplate, Error>.Failure(Errors.FailedToGetLocationPartition(queueName, locationId), State.Error);
        }

        projectCmdTemplate ??= await GetExistingProjectCommandTemplateAsync(templateName, projectId, resourceId, ct);
        if (projectCmdTemplate is not null && projectCmdTemplate.ProjectCommandTemplatePartitions.Any(a => a.LocationPartitionId == locationPartition.Id))
        {
            return Result<ProjectCommandTemplate, Error>.Success(projectCmdTemplate);
        }

        var state = DetermineTemplateState(heappeCommandTemplate.Value, projectCmdTemplate, defaultTemplateState);
        var projectCommandTemplatePartition = CreateTemplatePartition(heappeCommandTemplate, state, locationPartition);
        dbContext.Set<ProjectCommandTemplatePartition>().Add(projectCommandTemplatePartition);

        if (projectCmdTemplate is null)
        {
            projectCmdTemplate = commandTemplateDefinitionId.HasValue
                ? ProjectCommandTemplate.CreateWithDefinition(templateName, templateType, projectId, resourceId, commandTemplateDefinitionId.Value)
                : ProjectCommandTemplate.Create(templateName, templateType, projectId, resourceId);

            projectCmdTemplate.ProjectCommandTemplatePartitions.Add(projectCommandTemplatePartition);
            dbContext.Set<ProjectCommandTemplate>().Add(projectCmdTemplate);
        }
        else
        {
            projectCmdTemplate.ProjectCommandTemplatePartitions.Add(projectCommandTemplatePartition);
            dbContext.Set<ProjectCommandTemplate>().Update(projectCmdTemplate);
        }

        var changes = await dbContext.SaveChangesAsync(ct);
        if (changes == 0)
        {
            logger.LogProjectCommandTemplateCreationError(templateName, queueName, locationId);
            return Result<ProjectCommandTemplate, Error>.Failure(Errors.FailedToCreateProjectCommandTemplate(templateName, templateType, projectId, resourceId), State.Error);
        }

        return Result<ProjectCommandTemplate, Error>.Success(projectCmdTemplate);
    }

    /// <summary>
    /// Creates a command template in Heappe from either a definition or create model.
    /// </summary>
    private async Task<Result<CommandTemplateModel, Error>> CreateHEAppECommandTemplate(
        CommandTemplateDefinitionEntry templateDefinition,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateDefinition, nameof(templateDefinition));

        var result = templateDefinition.Type == ECommandTemplateType.Generic
            ? await CreateGenericTemplateAsync(templateDefinition, serviceConnectionInfo, heappeProject, nodeType, ct)
            : await CreateStandardTemplateAsync(templateDefinition, serviceConnectionInfo, heappeProject, nodeType, ct);

        if (!result.IsSuccess)
        {
            logger.LogHeappeCommandTemplateCreationError(templateDefinition.Name, heappeProject.Name, nodeType.Name);
        }

        return result;
    }

    private async Task<Result<CommandTemplateModel, Error>> CreateHEAppECommandTemplate(
        CommandTemplateCreateModel createModel,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(createModel, nameof(createModel));

        var executablePath = NormalizeNullableString(createModel.ExecutableFile);
        var preparationScript = NormalizeNullableString(createModel.PreparationScript);

        var result = await heappeService.CreateCommandTemplateAsync(
            serviceConnectionInfo,
            new CommandTemplateCreateModel(
                createModel.Name,
                createModel.Description,
                default,
                executablePath,
                preparationScript,
                heappeProject.Id,
                nodeType.Id,
                createModel.TemplateParameters,
                default
            ),
            ct);

        if (!result.IsSuccess)
        {
            logger.LogHeappeCommandTemplateCreationError(createModel.Name, heappeProject.Name, nodeType.Name);
        }

        return result;
    }

    /// <summary>
    /// Creates a command template in Heappe and synchronizes it with the local database.
    /// </summary>
    private async Task<Result<Error>> CreateCommandTemplateWithDefinition(
        CommandTemplateDefinitionEntry templateDefinition,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectCommandTemplate projectCmdTemplate,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        ECommandTemplateState defaultTemplateState,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateDefinition, nameof(templateDefinition));

        var createCommandTemplateResult = await CreateHEAppECommandTemplate(
            templateDefinition,
            serviceConnectionInfo,
            heappeProject,
            nodeType,
            ct);

        if (!createCommandTemplateResult.IsSuccess)
        {
            logger.LogFailedToCreateHeappeTemplate(templateDefinition.Name);
        }

        var projectCommandTemplateResult = await CreateProjectCommandTemplate(
            templateDefinition.Name,
            templateDefinition.Type,
            nodeType.Name,
            nodeType.ClusterNodeTypeAggregation?.Name,
            locationId,
            projectId,
            resourceId,
            heappeCommandTemplate: createCommandTemplateResult.Map(CommandTemplateCompactModel.FromCommandTemplateModel),
            projectCmdTemplate,
            templateDefinition.Id,
            defaultTemplateState,
            ct);

        if (!projectCommandTemplateResult.IsSuccess)
        {
            return Result<Error>.Failure(projectCommandTemplateResult.Error, projectCommandTemplateResult.State);
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Creates a command template in Heappe and synchronizes it with the local database.
    /// </summary>
    private async Task<Result<Error>> CreateCommandTemplate(
        CommandTemplateCreateModel templateCreateModel,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        Guid locationId,
        Guid projectId,
        Guid resourceId,
        ECommandTemplateState defaultTemplateState,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(templateCreateModel, nameof(templateCreateModel));

        var createCommandTemplateResult = await CreateHEAppECommandTemplate(
            templateCreateModel,
            serviceConnectionInfo,
            heappeProject,
            nodeType,
            ct);

        if (!createCommandTemplateResult.IsSuccess)
        {
            logger.LogFailedToCreateHeappeTemplate(templateCreateModel.Name);
        }

        var projectCommandTemplateResult = await CreateProjectCommandTemplate(
            templateCreateModel.Name,
            ECommandTemplateType.UserDefined,
            nodeType.Name,
            nodeType.ClusterNodeTypeAggregation?.Name,
            locationId,
            projectId,
            resourceId,
            heappeCommandTemplate: createCommandTemplateResult.Map(CommandTemplateCompactModel.FromCommandTemplateModel),
            default(ProjectCommandTemplate),
            default(Guid?),
            defaultTemplateState,
            ct);

        if (!projectCommandTemplateResult.IsSuccess)
        {
            return Result<Error>.Failure(projectCommandTemplateResult.Error, projectCommandTemplateResult.State);
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Creates command templates (on Heappe and/or locally) with automatic decision logic.
    /// Decision is based on null checks: creates Heappe if <see cref="CommandTemplateCreationRequest.ExistingHeappeTemplate"/> is null,
    /// local if <see cref="CommandTemplateCreationRequest.ExistingProjectTemplate"/> is null, or syncs state if both exist.
    /// </summary>
    public async Task<Result<Error>> CreateCommandTemplateAsync(
        CommandTemplateCreationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        if (request.TemplateDefinition is null && request.CreateModel is null)
        {
            return Result<Error>.Failure(Errors.MissingTemplateDefinitionAndCreateModel(), State.Error);
        }

        var shouldCreateHeappe = request.ExistingHeappeTemplate is null;
        var shouldCreateLocal = request.ExistingProjectTemplate is null;

        logger.LogCreatingCommandTemplate(shouldCreateHeappe, shouldCreateLocal);

        return (shouldCreateHeappe, shouldCreateLocal) switch
        {
            (false, false) => await HandleSyncStateOnlyAsync(request, ct),
            (true, false) => await HandleCreateHeappeOnlyAsync(request, ct),
            (false, true) => await HandleCreateLocalOnlyAsync(request, ct),
            (true, true) => await HandleCreateBothAsync(request, ct),
        };
    }

    /// <summary>
    /// Scenario 1 (false, false): Both templates already exist — synchronise partition state only.
    /// </summary>
    private Task<Result<Error>> HandleSyncStateOnlyAsync(
        CommandTemplateCreationRequest request,
        CancellationToken ct)
        => SyncCommandTemplateStateAsync(
            request.ExistingProjectTemplate!,
            request.ExistingHeappeTemplate!,
            request.NodeType.Name,
            request.NodeType.ClusterNodeTypeAggregation?.Name,
            request.LocationId,
            request.DefaultCommandTemplateState,
            ct);

    /// <summary>
    /// Scenario 2 (true, false): HEAppE template is missing — create it and sync the local partition state.
    /// Supports both <see cref="CommandTemplateCreationRequest.TemplateDefinition"/> (sync-service path)
    /// and <see cref="CommandTemplateCreationRequest.CreateModel"/> (HTTP endpoint path).
    /// </summary>
    private async Task<Result<Error>> HandleCreateHeappeOnlyAsync(
        CommandTemplateCreationRequest request,
        CancellationToken ct)
    {
        var (templateName, createResult) = request switch
        {
            { TemplateDefinition: { } def } => (def.Name, await CreateHEAppECommandTemplate(
                def, request.ServiceConnectionInfo, request.HeappeProject, request.NodeType, ct)),
            { CreateModel: { } model } => (model.Name, await CreateHEAppECommandTemplate(
                model, request.ServiceConnectionInfo, request.HeappeProject, request.NodeType, ct)),
            _ => throw new UnreachableException("Either TemplateDefinition or CreateModel must be provided")
        };

        if (!createResult.IsSuccess)
        {
            logger.LogFailedToCreateHeappeTemplate(templateName);

            if (request.ExistingProjectTemplate is { })
            {
                await UpdatePartitionStateOnFailureAsync(
                    request.ExistingProjectTemplate,
                    request.NodeType.Name,
                    request.NodeType.ClusterNodeTypeAggregation?.Name,
                    request.LocationId,
                    ct);
            }

            return Result<Error>.Failure(createResult.Error, createResult.State);
        }

        return request.ExistingProjectTemplate is { }
            ? await SyncCommandTemplateStateAsync(
                    request.ExistingProjectTemplate,
                    CommandTemplateCompactModel.FromCommandTemplateModel(createResult.Value),
                    request.NodeType.Name,
                    request.NodeType.ClusterNodeTypeAggregation?.Name,
                    request.LocationId,
                    request.DefaultCommandTemplateState,
                    ct)
            : Result<Error>.Success();
    }

    /// <summary>
    /// Scenario 3 (false, true): Local record is missing — create it using the existing HEAppE template.
    /// Supports both <see cref="CommandTemplateCreationRequest.TemplateDefinition"/> (sync-service path)
    /// and <see cref="CommandTemplateCreationRequest.CreateModel"/> (HTTP endpoint path).
    /// </summary>
    private async Task<Result<Error>> HandleCreateLocalOnlyAsync(
        CommandTemplateCreationRequest request,
        CancellationToken ct)
    {
        (string templateName, ECommandTemplateType templateType, ProjectCommandTemplate commandTemplate, Guid? commandTemplateDefinitionId) = request switch
        {
            { TemplateDefinition: { } def } => (def.Name, def.Type, request.ExistingProjectTemplate, (Guid?)def.Id),
            { CreateModel: { } model } => (model.Name, ECommandTemplateType.UserDefined, default, default(Guid?)),
            _ => throw new UnreachableException("Either TemplateDefinition or CreateModel must be provided")
        };

        var heappeTemplateResult = request.ExistingHeappeTemplate is { }
            ? Result<CommandTemplateCompactModel, Error>.Success(request.ExistingHeappeTemplate)
            : Result<CommandTemplateCompactModel, Error>.Failure(Errors.MissingHeappeCommandTemplates(), State.Error);

        var result = await CreateProjectCommandTemplate(
            templateName,
            templateType,
            request.NodeType.Name,
            request.NodeType.ClusterNodeTypeAggregation?.Name,
            request.LocationId,
            request.ProjectId,
            request.ResourceId,
            heappeTemplateResult,
            commandTemplate,
            commandTemplateDefinitionId,
            request.DefaultCommandTemplateState,
            ct);

        if (!result.IsSuccess)
        {
            logger.LogFailedToCreateLocalTemplate(templateName);
            return Result<Error>.Failure(result.Error, result.State);
        }

        return Result<Error>.Success();
    }

    /// <summary>
    /// Scenario 4 (true, true): Neither template exists — create both HEAppE and local records.
    /// Delegates to the appropriate method based on whether a definition or create model is provided.
    /// </summary>
    private Task<Result<Error>> HandleCreateBothAsync(
        CommandTemplateCreationRequest request,
        CancellationToken ct)
        => request switch
        {
            { TemplateDefinition: { } def } => CreateCommandTemplateWithDefinition(
                def,
                request.ServiceConnectionInfo,
                request.ExistingProjectTemplate,
                request.HeappeProject,
                request.NodeType,
                request.LocationId,
                request.ProjectId,
                request.ResourceId,
                request.DefaultCommandTemplateState,
                ct),
            { CreateModel: { } model } => CreateCommandTemplate(
                model,
                request.ServiceConnectionInfo,
                request.HeappeProject,
                request.NodeType,
                request.LocationId,
                request.ProjectId,
                request.ResourceId,
                request.DefaultCommandTemplateState,
                ct),
            _ => throw new UnreachableException("Either TemplateDefinition or CreateModel must be provided")
        };

    /// <summary>
    /// Synchronizes the state and ID of a command template partition with Heappe template state.
    /// </summary>
    private async Task<Result<Error>> SyncCommandTemplateStateAsync(
        ProjectCommandTemplate projectTemplate,
        CommandTemplateCompactModel heappeTemplate,
        string queueName,
        string? aggregationName,
        Guid locationId,
        ECommandTemplateState defaultTemplateState,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projectTemplate, nameof(projectTemplate));
        ArgumentNullException.ThrowIfNull(heappeTemplate, nameof(heappeTemplate));

        var partition = projectTemplate.ProjectCommandTemplatePartitions.FirstOrDefault(f =>
            f.LocationPartition.Name == queueName
            && f.LocationPartition.AggregationName == aggregationName
            && f.LocationPartition.LocationId == locationId);

        if (partition is null)
        {
            logger.LogLocationPartitionRetrievalFailure(projectTemplate.Name, queueName, locationId);
            return Result<Error>.Failure(Errors.FailedToGetLocationPartition(queueName, locationId), State.Error);
        }

        var newState = DetermineTemplateState(heappeTemplate, projectTemplate, defaultTemplateState);

        if (partition.State == newState && partition.CommandTemplateId == heappeTemplate.Id)
        {
            return Result<Error>.Success();
        }

        partition.State = newState;
        partition.CommandTemplateId = heappeTemplate.Id;

        var changes = await dbContext.Set<ProjectCommandTemplatePartition>()
            .Where(p => p.Id == partition.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.State, newState)
                .SetProperty(p => p.CommandTemplateId, heappeTemplate.Id),
            ct);

        if (changes == 0)
        {
            logger.LogCommandTemplateStateUpdateFailure(projectTemplate.Name, heappeTemplate.Id, queueName, locationId);
            return Result<Error>.Failure(
                Errors.FailedToUpdateCommandTemplateState(
                    projectTemplate.Name,
                    projectTemplate.Type,
                    heappeTemplate.Id,
                    projectTemplate.ProjectId,
                    projectTemplate.ProjectResourceId),
                State.Error);
        }


        return Result<Error>.Success();
    }

    private async Task<LocationPartition?> GetLocationPartitionAsync(
        string queueName,
        string? aggregationName,
        Guid locationId,
        CancellationToken ct) =>
        await dbContext.Set<LocationPartition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Name == queueName && f.AggregationName == aggregationName && f.LocationId == locationId, ct);

    private async Task<ProjectCommandTemplate?> GetExistingProjectCommandTemplateAsync(
        string templateName,
        Guid projectId,
        Guid resourceId,
        CancellationToken ct) =>
        await dbContext.Set<ProjectCommandTemplate>()
            .FirstOrDefaultAsync(f =>
                f.Name == templateName
                && f.ProjectId == projectId
                && f.ProjectResourceId == resourceId,
                ct);


    private static ECommandTemplateState DetermineTemplateState(CommandTemplateCompactModel heappeCommandTemplateResult, ProjectCommandTemplate? projectCommandTemplate, ECommandTemplateState defaultTemplateState) =>
        (heappeCommandTemplateResult, projectCommandTemplate) switch
        {
            (null, _) => ECommandTemplateState.Pending,
            ({ IsEnabled: true }, { State: ECommandTemplateState.Active }) => ECommandTemplateState.Active,
            ({ IsEnabled: true }, null) => defaultTemplateState,
            _ => ECommandTemplateState.Inactive
        };

    private static ProjectCommandTemplatePartition CreateTemplatePartition(
        Result<CommandTemplateCompactModel, Error> heappeCommandTemplate,
        ECommandTemplateState state,
        LocationPartition locationPartition) =>
        new()
        {
            CommandTemplateId = heappeCommandTemplate is { IsSuccess: true } ? heappeCommandTemplate.Value.Id : default,
            State = state,
            LocationPartitionId = locationPartition.Id,
        };

    private async Task<Result<CommandTemplateModel, Error>> CreateGenericTemplateAsync(
        CommandTemplateDefinitionEntry templateDefinition,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        CancellationToken ct)
    {
        var preparationCommand = NormalizeNullableString(templateDefinition.PreparationCommand);

        return await heappeService.CreateGenericCommandTemplateAsync(
            serviceConnectionInfo,
            new GenericCommandTemplateCreateModel(
                templateDefinition.Name,
                templateDefinition.Description,
                default,
                preparationCommand,
                heappeProject.Id,
                nodeType.Id
            ),
            ct);
    }

    /// <summary>
    /// Creates a standard command template in HEAppE from a template definition.
    /// The definition's ExecutionCommand and PreparationCommand must contain COMPLETE paths (not just filenames).
    /// </summary>
    private async Task<Result<CommandTemplateModel, Error>> CreateStandardTemplateAsync(
        CommandTemplateDefinitionEntry templateDefinition,
        BaseHeappeConnectionInfo serviceConnectionInfo,
        ProjectModel heappeProject,
        NodeTypeModel nodeType,
        CancellationToken ct)
    {
        var executablePath = NormalizeNullableString(templateDefinition.ExecutionCommand);
        var preparationCommand = NormalizeNullableString(templateDefinition.PreparationCommand);
        var parameters = templateDefinition.Parameters
            .Select(s => new CommandTemplateParameterCreateModel(s.Name, s.Description, null))
            .ToList();

        return await heappeService.CreateCommandTemplateAsync(
            serviceConnectionInfo,
            new CommandTemplateCreateModel(
                templateDefinition.Name,
                templateDefinition.Description,
                default,
                executablePath,
                preparationCommand,
                heappeProject.Id,
                nodeType.Id,
                parameters,
                default
            ),
            ct);
    }

    private static string? NormalizeNullableString(string? value) =>
        string.IsNullOrEmpty(value) ? default : value;
}