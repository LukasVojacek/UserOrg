using System.Diagnostics.CodeAnalysis;

using Core.Domain.Shared;
using Core.Shared.OutputCache;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.Http;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.Features.Project.CreateProject;

/// <summary>
/// Creates a project and grants the creator the project owner role. Mirrors legacy
/// <c>POST api/Project</c> + the implicit owner assignment that followed it.
/// </summary>
internal static class CreateProjectEndpoint
{
    internal sealed class AuditCategory;

    internal sealed record Request(
        string ShortName,
        string Name,
        string? Description,
        string? ContactEmail,
        DateTimeOffset StartDate,
        DateTimeOffset EndDate,
        string DomainTypeName);

    internal sealed record Response(string ShortName);

    private sealed record ValidatedRequest(
        ProjectShortName ShortName,
        ProjectName Name,
        Description? Description,
        Email? ContactEmail,
        DomainTypeName DomainTypeName);

    internal static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        Caller caller,
        [FromServices] IRepository<ProjectAggregate, ProjectShortName> repository,
        [FromServices] IRepository<Domain.User.User, IdpId> users,
        [FromServices] IUserDirectory userDirectory,
        [FromServices] IProjectDomainTypeLookup domainTypes,
        [FromServices] IRoleListService roleListService,
        [FromServices] IOptions<WellKnownRoles> roleOptions,
        [FromServices] IUnitOfWork unitOfWork,
        [FromServices] IOutputCacheInvalidator cache,
        [FromServices] IAuditLogger<AuditCategory> auditLogger,
        [FromServices] ILogger<AuditCategory> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!TryValidate(request, out var validated, out var invalid))
            return invalid;

        if (await userDirectory.FindUserIdAsync(caller.IdpId, ct) is not { } createdBy)
            return Problems.CallerNotProvisioned();

        var founder = await users.FindAsync(caller.IdpId, ct);
        if (founder is null)
            return Problems.NotFound(CommonErrorCodes.CallerNotProvisioned, "The calling identity is not provisioned.");

        var domainType = await domainTypes.FindByNameAsync(validated.DomainTypeName, ct);
        if (domainType is null)
            return Problems.Validation(ProjectErrorCodes.DomainTypeNotFound, "Project domain type does not exist.");

        var roles = await roleListService.ListRoles(ct);
        if (!TryFindOwnerRole(roles, roleOptions.Value, out var ownerRole, out var roleProblem))
            return roleProblem;

        if (await repository.FindAsync(validated.ShortName, ct) is not null)
            return Problems.Conflict("project.short_name_already_exists", $"A project with short name '{validated.ShortName.Value}' already exists.");

        var project = ProjectAggregate.Create(
            validated.ShortName,
            validated.Name,
            domainType,
            createdBy,
            request.StartDate,
            request.EndDate,
            ServiceName.Default,
            EOperationMode.Editable,
            validated.Description,
            validated.ContactEmail);

        return await PersistWithOwnerAsync(
            project, founder, createdBy, ProjectRoleId.Create(ownerRole.Id), caller, repository, unitOfWork, cache, auditLogger, logger, httpContext, ct);
    }

    private static bool TryValidate(
        Request request,
        [NotNullWhen(true)] out ValidatedRequest? validated,
        [NotNullWhen(false)] out IResult? problem)
    {
        validated = null;
        problem = null;

        if (!ProjectShortName.TryParse(request.ShortName, null, out var shortName))
            problem = Problems.Validation(ProjectErrorCodes.ShortNameInvalid, "Project short name is required.");
        else if (!ProjectName.TryCreate(request.Name, out var projectName, out var projectNameError))
            problem = Problems.Validation(ProjectErrorCodes.NameInvalid, projectNameError);
        else if (!TryParseDescription(request.Description, out var description))
            problem = Problems.Validation(ProjectErrorCodes.DescriptionTooLong, "Description must not exceed 500 characters.");
        else if (!TryParseContactEmail(request.ContactEmail, out var email))
            problem = Problems.Validation(ProjectErrorCodes.ContactEmailInvalid, "Invalid email address.");
        else if (!DomainTypeName.TryCreate(request.DomainTypeName, out var domainTypeName, out var domainTypeError))
            problem = Problems.Validation(ProjectErrorCodes.DomainTypeNameInvalid, domainTypeError);
        else if (request.EndDate < request.StartDate)
            problem = Problems.Validation(ProjectErrorCodes.EndDateBeforeStartDate, "End date cannot be earlier than start date.");
        else
            validated = new ValidatedRequest(shortName, projectName, description, email, domainTypeName);

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

    private static bool TryFindOwnerRole(
        Result<IList<RoleInfo>, Error> roles,
        WellKnownRoles roleNames,
        [NotNullWhen(true)] out RoleInfo? ownerRole,
        [NotNullWhen(false)] out IResult? problem)
    {
        ownerRole = null;
        if (!roles.IsSuccess)
        {
            problem = Problems.RoleServiceUnavailable();
            return false;
        }

        ownerRole = roles.Value.FindByName(roleNames.DefaultProjectOwnerRoleName);
        if (ownerRole is null)
        {
            problem = Problems.Upstream(CommonErrorCodes.RoleServiceUnavailable, "Project owner role is not configured.");
            return false;
        }

        problem = null;
        return true;
    }

    private static async Task<IResult> PersistWithOwnerAsync(
        ProjectAggregate project,
        Domain.User.User founder,
        UserId createdBy,
        ProjectRoleId ownerRoleId,
        Caller caller,
        IRepository<ProjectAggregate, ProjectShortName> repository,
        IUnitOfWork unitOfWork,
        IOutputCacheInvalidator cache,
        IAuditLogger<AuditCategory> auditLogger,
        ILogger<AuditCategory> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        await repository.AddAsync(project, ct);

        // Grant the founder the owner role (membership + project role) before the single commit, so a
        // project is never persisted without its owner. AddAsync assigns the ProjectId client-side, so
        // no intermediate save is needed to resolve the membership/role FKs.
        var grant = project.AssignUserWithRole(founder, createdBy, ownerRoleId);
        if (!grant.IsSuccess)
            return grant.Error.ToProblem();

        await unitOfWork.SaveChangesAsync(ct);
        PermissionsChangedHeader.Append(httpContext, [caller.IdpId.Value]);
        await cache.EvictAsync(CacheTags.UserInfo(caller.IdpId.Value), ct);
        await auditLogger.AuditBestEffortAsync(CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectSelfCreated, caller, project.ShortName) with
        {
            TargetType = AuditActions.TargetTypes.Project,
            TargetId = project.ShortName.Value,
        }, logger);

        return Results.Created($"v2/Projects/{project.ShortName.Value}", new Response(project.ShortName.Value));
    }

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapCreateProjectEndpoint()
        {
            app.MapPost("Projects", HandleAsync)
                .DisabledWhen("ReadOnlyMode")
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("CreateProjectV2")
            .WithSummary("Create a project")
            .WithDescription("Creates a project and grants the creator the project owner role. Body {ShortName, Name, Description?, ContactEmail?, StartDate, EndDate, DomainTypeName}. Returns 201; 400 on validation / unknown domain type; 409 (code project.short_name_already_exists) when the short name already exists. Errors carry a machine-readable code in the problem extensions — branch on that, not the message.")
            .WithTags("Project")
            .Accepts<Request>("application/json")
            .Produces<Response>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}