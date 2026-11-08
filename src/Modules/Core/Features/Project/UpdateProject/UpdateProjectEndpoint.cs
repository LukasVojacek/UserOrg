using System.Diagnostics.CodeAnalysis;

using Core.Domain.Shared;
using Core.Shared.OutputCache;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

using UserOrg.Shared.Extensions;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.UpdateProject;

internal static class UpdateProjectEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string Name,
        string? Description,
        string? ContactEmail,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate,
        string DomainTypeName);

    internal static async Task<IResult> HandleAsync(
        [FromRoute] ProjectShortName projectShortName,
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IProjectDomainTypeLookup domainTypes,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        CancellationToken ct)
    {
        if (caller.RequireProjectScope(projectShortName) is { } denied)
        {
            await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeDenied(
                AuditActions.ProjectSelfUpdated, caller, projectShortName) with
            {
                TargetType = AuditActions.TargetTypes.Project,
                TargetId = projectShortName.Value,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            }, logger);
            return denied;
        }

        // Validate request shape up front (VO factories throw, which the global handler maps to 500).
        if (!TryValidate(request, out var validated, out var invalid))
            return invalid;

        var project = await repository.FindAsync(projectShortName, ct);
        if (project is null)
            return Problems.ProjectNotFound();

        var domainType = await domainTypes.FindByNameAsync(validated.DomainTypeName, ct);
        if (domainType is null)
            return Problems.Validation(ProjectErrorCodes.DomainTypeNotFound, "Project domain type does not exist.");

        // Apply mutations; the first invariant violation (e.g. read-only) short-circuits to a problem.
        if (FirstFailure(project, request, validated, domainType) is { } failure)
            return failure.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        await cache.EvictAsync(CacheTags.Project(projectShortName.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectSelfUpdated, caller, projectShortName) with
        {
            TargetType = AuditActions.TargetTypes.Project,
            TargetId = projectShortName.Value,
        }, logger);

        return Results.NoContent();
    }

    private sealed record ValidatedRequest(Description? Description, Email? ContactEmail, DomainTypeName DomainTypeName);

    private static bool TryValidate(
        Request request,
        [NotNullWhen(true)] out ValidatedRequest? validated,
        [NotNullWhen(false)] out IResult? problem)
    {
        validated = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100)
            problem = Problems.Validation(ProjectErrorCodes.NameInvalid, "Project name is required and must not exceed 100 characters.");
        else if (!TryParseDescription(request.Description, out var description))
            problem = Problems.Validation(ProjectErrorCodes.DescriptionTooLong, "Description must not exceed 500 characters.");
        else if (!TryParseContactEmail(request.ContactEmail, out var email))
            problem = Problems.Validation(ProjectErrorCodes.ContactEmailInvalid, "Invalid email address.");
        else if (!DomainTypeName.TryCreate(request.DomainTypeName, out var domainTypeName, out var domainTypeError))
            problem = Problems.Validation(ProjectErrorCodes.DomainTypeNameInvalid, domainTypeError);
        else
            validated = new ValidatedRequest(description, email, domainTypeName);

        return validated is not null;
    }

    private static bool TryParseDescription(string? raw, out Description? description)
    {
        description = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (raw.Length > 500)
            return false;
        description = Description.Create(raw);
        return true;
    }

    private static bool TryParseContactEmail(string? raw, out Email? email)
    {
        email = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;
        if (!Email.TryParse(raw, null, out var parsed))
            return false;
        email = parsed;
        return true;
    }

    private static Result<Error>? FirstFailure(
        ProjectAggregate project, Request request, ValidatedRequest validated, ProjectDomainType domainType)
    {
        Func<Result<Error>>[] mutations =
        [
            () => project.Rename(ProjectName.Create(request.Name)),
            () => project.UpdateDescription(validated.Description),
            () => project.ChangeContactEmail(validated.ContactEmail),
            () => project.Reschedule(request.StartDate, request.EndDate),
            () => project.ChangeDomainType(domainType),
        ];

        return mutations.Select(mutate => mutate()).FirstOrDefault(result => !result.IsSuccess);
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapUpdateProjectEndpoint()
        {
            app.MapPut("Projects/{projectShortName}", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("UpdateProjectV2")
            .WithSummary("Update a project")
            .WithDescription("Updates a project's name, description, contact email, validity period and domain type. The short name is immutable.")
            .WithTags("Project")
            .Accepts<Request>("application/json")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}