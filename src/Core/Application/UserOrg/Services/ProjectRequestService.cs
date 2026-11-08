using System.Text;
using System.Text.Json;

using FluentValidation;
using FluentValidation.Results;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.LogMessages;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Domain;
using UserOrganizationManagement.Domain.Configuration;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Request;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Application.UserOrg.Services;

public class ProjectRequestService : IProjectRequestService
{
    private const string emailSubject = "Project Assignment Request";
    private const string htmlEndpoint = "api/Email/Html";
    private readonly ILogger<ProjectRequestService> _logger;
    private readonly IActionLinkService _actionLinkService;
    private readonly IUserOrgUnitOfWork _uow;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IOptions<Domain.Configuration.ActionLink> linkOptions;
    private readonly IOptions<MailConfiguration> mailOptions;
    private readonly IProjectService _service;
    private readonly IProviderRepository _providerRepository;
    private readonly IProjectResourceRepository _resourceRepository;
    private readonly IRoleListService _roleListService;
    private readonly IUserOrgDbContext _dbContext;
    private readonly MailConfiguration _mailConfiguration;
    private readonly HttpClient _emailHttpClient;
    private readonly string _actionValidHours;

    public ProjectRequestService(ILogger<ProjectRequestService> logger, IActionLinkService actionLinkService, IUserOrgUnitOfWork userOrgUnitOfWork, IHttpClientFactory httpClientFactory, IOptions<Domain.Configuration.ActionLink> linkOptions, IOptions<MailConfiguration> mailOptions, IProjectService service, IProviderRepository providerRepository, IProjectResourceRepository resourceRepository, IRoleListService roleListService, IUserOrgDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(mailOptions);
        ArgumentNullException.ThrowIfNull(linkOptions);
        _logger = logger;
        _actionLinkService = actionLinkService;
        _uow = userOrgUnitOfWork;
        this.httpClientFactory = httpClientFactory;
        this.linkOptions = linkOptions;
        this.mailOptions = mailOptions;
        _service = service;
        _providerRepository = providerRepository;
        _resourceRepository = resourceRepository;
        this._roleListService = roleListService;
        _dbContext = dbContext;
        _mailConfiguration = mailOptions.Value;
        _emailHttpClient = httpClientFactory.CreateClient(Constants.MailSenderHttpClientName);
        _actionValidHours = $"{linkOptions.Value.DurationInHours}";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "<Pending>")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2234:Pass system uri objects instead of strings", Justification = "<Pending>")]
    private async Task SendEmail(string receiverEmail, string templateName, string[] templateArgs, string[]? ccEmail, CancellationToken ct = default)
    {

        var requestBody = new
        {
            Sender = _mailConfiguration.SenderEmail,
            Subject = emailSubject,
            Receivers = new string[1] { receiverEmail },
            TemplateName = templateName,
            CC = ccEmail,
            TemplateArguments = templateArgs,
            Priority = 1
        };
        var serializedRequest = JsonSerializer.Serialize(requestBody);
        _logger.LogInformation("Sending email request to emailsender. {@details}", requestBody);
        using var content = new StringContent(serializedRequest, Encoding.Default, "application/json");
        var response = await _emailHttpClient.PostAsync(htmlEndpoint, content, ct);
        _logger.LogInformation("Sending email response: {StatusCode}", (int)response.StatusCode);
    }

    private Task SendAcceptedEmailToUser(Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        (var templateName, var templateParams) = request switch
        {
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.Accepted } => ("RequestState", [request.Id.ToString(), request.Project.ShortName, request.State.ToString()]),
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.PendingForHPCProject } => ("RequestState", [request.Id.ToString(), request.Project.ShortName, request.State.ToString()]),
            { Type: ERequestType.SelfOnboarding, State: ERequestState.Accepted } => ("RequestState", new string[] { request.Id.ToString(), request.Project.ShortName, request.State.ToString() }),
            _ => throw new InvalidDataException("Unable to accept request that is not in pending state")
        };

        return SendEmail(request.User.Email, templateName, templateParams, null, ct);
    }

    private Task SendRequestCreatedEmailToUser(Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        (var templateName, var templateParams) = request switch
        {
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.PendingForHPCProject } => ("NewProjectAssignment", [request.Id.ToString(), request.Project.ShortName]),
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.PendingForPIAccept } => ("NewProjectAssignment", [request.Id.ToString(), request.Project.ShortName, request.ProjectResourceName]),
            { Type: ERequestType.SelfOnboarding, State: ERequestState.Pending } => ("NewUserAssignment", new string[] { request.Id.ToString(), request.Project.ShortName }),
            _ => throw new InvalidDataException("Unable to send email with input data.")
        };

        return SendEmail(request.User.Email, templateName, templateParams, request.State == ERequestState.PendingForHPCProject ? [request.User.Email] : null, ct);
    }
    private Task SendRequestCreatedEmailToPI(Request request, string validForHours, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Project.Email);

        if (request.Type is not ERequestType.ProjectResourceAssignment)
        {
            return Task.CompletedTask;
        }

        (var templateName, var templateParams) = request switch
        {
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.PendingForHPCProject } => ("NewProjectAssignment_PM", new string[] { request.Id.ToString(), _mailConfiguration.PortalAddress, request.Project.ShortName, validForHours }),
            { Type: ERequestType.ProjectResourceAssignment, State: ERequestState.PendingForPIAccept } => ("NewProjectAssignment_PM", [request.Id.ToString(), _mailConfiguration.PortalAddress, request.Project.ShortName, validForHours]),

            _ => throw new InvalidDataException("Unable to send email with input data.")
        };

        return SendEmail(request.ContactEmail, templateName, templateParams, null, ct);
    }

    private Task SendRequestCreatedEmailToProjectOwner(Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Project.Email);

        if (request.Type is not ERequestType.SelfOnboarding)
        {
            return Task.CompletedTask;
        }

        (var templateName, var templateParams) = ("NewUserAssignment_PM", new string[] { request.Id.ToString(), request.Project.ShortName });

        return SendEmail(request.Project.CreatedByUser.Email, templateName, templateParams, null, ct);
    }

    private Task SendRequestEmailToHpcContact(Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        return request is not { Type: ERequestType.ProjectResourceAssignment, ProjectResource: null, State: ERequestState.PendingForHPCProject }
            ? Task.CompletedTask
            : SendEmail(request.Provider.SupportEmail, "NewProjectAssignment", [request.Id.ToString(), request.Project.ShortName.Value, request.ProjectResourceName], [request.ContactEmail], ct);
    }

    private Task SendRejectEmailToUser(Request request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        return SendEmail(request.User.Email, "RequestState", [request.Id.ToString(), request.Project.ShortName, request.State.ToString()], null, ct);
    }

    public async Task<ERequestState> AcceptAssignRequestAsync(Guid requestId, Guid assignedByUserId, CancellationToken ct = default)
    {
        var request = await _uow.RequestRepository.GetByIdWithUserAndProjectUser(requestId, ct)
            ;
        RequestException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        if (request is { Type: ERequestType.SelfOnboarding, State: ERequestState.Pending, Project: { } project, User: { } user })
        {
            var roles = await _roleListService.ListRoles(ct);
            if (!roles.IsSuccess)
            {
                throw new ArgumentException("Failed to retrieve default project role");
            }
            var defaultProjectRole = roles.Value.FirstOrDefault(r => r.Name.Equals(RoleInfoConstants.ProjectMemberRoleName, StringComparison.OrdinalIgnoreCase));

            try
            {
                await _service.AssignedUserToProjectWithRoleAsync(project.ShortName, user.IdentityProviderUserId, assignedByUserId, defaultProjectRole.Name, ct);
            }
            // Retry of an accept whose assignment already committed but whose state change did not.
            // The filter is load-bearing, not defensive: ProjectService.AssignedUserToProjectWithRoleAsync raises
            // this same exception for an already-active member and returns before ever writing the role, so the
            // swallow is allowed only where the postcondition is actually persisted. Without it, a member whose
            // role was never written would be accepted with no role granted.
            catch (RecordAlreadyExistException ex) when (AssignmentWithRoleAlreadyPersisted(project, user))
            {
                // Only the state change is left to finish. Cache invalidation is not repeated here: the first
                // attempt performs it immediately after the assignment save.
                _logger.UserAlreadyAssignedOnAccept(request.Id, ex.GetType().Name);
            }

            request.State = ERequestState.Accepted;

            // Deliberately NOT wrapped in a transaction, unlike the resource path
            // (AssignResourceAndAcceptAsync): AssignedUserToProjectWithRoleAsync calls the role service
            // over HTTP, so a transaction spanning these two saves would be held open across a remote
            // call - a shape nothing else in this repo uses. The split this leaves between the
            // assignment save and the state save is a known, tracked follow-up, not an oversight.
            var changes = await _uow.SaveAsync(ct);
            DatabaseCommitException.ThrowIfLessThan(1, changes);

            await SendAcceptedEmailToUser(request, ct);
            return request.State;
        }

        return await AcceptResourceAssignmentAsync(request, ct);
    }

    public async Task<ERequestState> AcceptAssignRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.RequestRepository.GetByIdWithUserAndProjectUser(requestId, ct)
            ;
        RequestException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        return await AcceptResourceAssignmentAsync(request, ct);
    }

    /// <summary>
    /// POSTCONDITION check for <see cref="IProjectService.AssignedUserToProjectWithRoleAsync"/>: true when the
    /// user's membership of the project and an active permission carrying a role are both persisted. It does
    /// not tell a retry from a first-ever request - no data can - but it rejects the case that matters, a
    /// membership whose role was never written, which would otherwise be accepted with no role granted.
    /// </summary>
    /// <remarks>
    /// The failed call loaded members and permissions into the same tracked <see cref="Project"/> instance,
    /// so these navigations show the persisted state. The role is required to be non-null because
    /// <see cref="IProjectService.AssignedUserToProjectWithRoleAsync"/> inserts the permission row before it
    /// assigns the role, so a role-less row is exactly a half-written assignment.
    /// </remarks>
    private static bool AssignmentWithRoleAlreadyPersisted(Project project, User user) =>
        project.AssignedUsers.Any(a => !a.IsDeleted && a.User?.IdentityProviderUserId == user.IdentityProviderUserId)
        && project.UserPermissions.Any(p => !p.IsDeleted && p.SystemRoleId is not null && p.User?.IdentityProviderUserId == user.IdentityProviderUserId);

    private async Task<ERequestState> AcceptResourceAssignmentAsync(Request request, CancellationToken ct)
    {
        if (request is { Type: ERequestType.ProjectResourceAssignment, ProjectResource: { } resource, Project: { } project, State: ERequestState.PendingForPIAccept or ERequestState.PendingForHPCProject })
        {
            await AssignResourceAndAcceptAsync(request, project, resource, ct);
        }
        else if (request is { Type: ERequestType.ProjectResourceAssignment, ProjectResource: null, State: ERequestState.PendingForPIAccept })
        {
            // Requested resource does not exist yet: park the request for the HPC side.
            request.State = ERequestState.PendingForHPCProject;
            var changes = await _uow.SaveAsync(ct);
            DatabaseCommitException.ThrowIfLessThan(1, changes);
        }

        // Deliberately outside the transaction AssignResourceAndAcceptAsync opened, after its commit: a
        // mail-server outage must not roll back an accept the assignment has already granted (issue #278).
        await SendRequestEmailToHpcContact(request, ct);
        await SendAcceptedEmailToUser(request, ct);
        return request.State;
    }

    /// <summary>
    /// Assigns the resource and accepts the request as one unit. The assignment is saved by
    /// <see cref="IProjectService.AssignedProjectResourceToProjectAsync"/> and the state by this method, so
    /// without a transaction a failure between them leaves the resource attached to a request that is still
    /// pending (issue #278). The injected context is the same scoped instance the unit of work saves through.
    /// See docs/architecture/adr/0087-transacted-resource-accept-and-conditional-link-burn.md, which also
    /// records why the SelfOnboarding branch above stays un-transacted.
    /// </summary>
    private async Task AssignResourceAndAcceptAsync(Request request, Project project, ProjectResource resource, CancellationToken ct)
    {
        await using var transaction = await _dbContext.BeginTransactionAsync(ct);
        try
        {
            await AssignResourceAllowingAlreadyAssignedAsync(request, project, resource, ct);

            request.State = ERequestState.Accepted;
            var changes = await _uow.SaveAsync(ct);
            DatabaseCommitException.ThrowIfLessThan(1, changes);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await RollbackPreservingFailureAsync(transaction, request.Id);
            // After a rollback the tracked entities claim to be persisted while the database has reverted
            // them; a later save in this scope would act on that stale state.
            _dbContext.ClearChangeTracker();
            throw;
        }
    }

    /// <summary>
    /// Rolls back without letting the rollback replace the failure that caused it. A failed commit has
    /// already aborted the transaction, so <see cref="IDbContextTransaction.RollbackAsync"/> throws in turn;
    /// and the request's token may be the very thing that was cancelled, which is why the rollback does not
    /// take it. Disposal rolls the transaction back either way, so a failure here costs diagnosis, not
    /// integrity.
    /// </summary>
    private async Task RollbackPreservingFailureAsync(IDbContextTransaction transaction, Guid requestId)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // Narrowing this would let some rollback failures escape and mask the original one
        catch (Exception rollbackFailure)
#pragma warning restore CA1031
        {
            _logger.RollbackAfterFailedAcceptIncomplete(rollbackFailure, requestId);
        }
    }

    private async Task AssignResourceAllowingAlreadyAssignedAsync(Request request, Project project, ProjectResource resource, CancellationToken ct)
    {
        try
        {
            // ProjectService owns the resource → project attachment (incl. reactivation and validation).
            await _service.AssignedProjectResourceToProjectAsync(project.ShortName, resource.Name!, null, ct);
        }
        catch (RecordAlreadyExistException ex)
        {
            // Retry of an accept whose assignment already committed but whose state change did not.
            // Unfiltered on purpose, unlike the SelfOnboarding branch above: here the exception is
            // raised only for an already-active assignment (ProjectService.AssignedProjectResourceToProjectAsync),
            // so it is itself proof of the postcondition. Only the state change is left to finish. Cache
            // invalidation is not repeated here: the first attempt performs it immediately after the
            // assignment save.
            _logger.ResourceAlreadyAssignedOnAccept(request.Id, ex.GetType().Name);
        }
    }

    public async Task AddAssignRequestAsync(ProjectShortName projectShortName, string idpUserId, ERequestType type, string? projectResourceName, Guid? providerId, string? contactEmail, HostString baseUri, CancellationToken ct)
    {
        var project = await _uow.ProjectRepository.GetProjectByShortNameWithCreatedUsersAsync(projectShortName, ct);
        ProjectNotFoundException.ThrowIfNull(project);

        var user = await _uow.UserRepository.GetBySidWithProjectsAsync(idpUserId, ct);
        UserNotFoundException.ThrowIfNull(user);
        var projectResource = await _resourceRepository.GetByNameWithIncludesAsync(projectResourceName, ct);

        if (!string.IsNullOrEmpty(contactEmail) && projectResource is { } && projectResource.PrimaryInvestigatorEmail != contactEmail)
        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(contactEmail), $"Incorrect PI email for resource") });
        }

        ArgumentException.ThrowIfNullOrEmpty(user.Email);
        ArgumentException.ThrowIfNullOrEmpty(project.CreatedByUser.Email);

        if (type == ERequestType.SelfOnboarding && project.AssignedUsers.Any(x => x.User == user && !x.IsDeleted))
        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(user), $"{user.UserName} is already assigned to {projectShortName}") });
        }

        if (type == ERequestType.ProjectResourceAssignment && projectResourceName is null)
        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(projectResourceName), $"ProjectResourceName must be filled to create Request for ProjectResourceAssignment") });
        }

        if (type == ERequestType.ProjectResourceAssignment && project.AssignedProjectResources.Any(x => x.ProjectResource.Name == projectResourceName && !x.IsDeleted))
        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(user), $"{projectResourceName} is already assigned to {projectShortName}") });
        }

        var existingRequest = await _uow.RequestRepository.GetRequestsForUser(user.IdentityProviderUserId, ct);

        if (existingRequest.Count > 0
            && existingRequest.Any(x =>
                x.Type == type &&
                x.State is ERequestState.Pending or ERequestState.PendingForPIAccept or ERequestState.PendingForHPCProject &&
                x.Project.ShortName == projectShortName &&
                x.ProjectResourceName == projectResourceName))

        {
            throw new ValidationException(new ValidationFailure[1] { new(nameof(user), $"{user.UserName} has already pending request for assignment to {projectShortName}") });
        }

        var provider = providerId.HasValue ? await _providerRepository.GetByIdAsync(providerId.Value, ct) : null;

        ArgumentException.ThrowIfNullOrEmpty(user.Email);
        ArgumentException.ThrowIfNullOrEmpty(project.CreatedByUser.Email);

        var newRequest = new Request
        {
            Project = project,
            User = user,
            Type = type,
            State = (type, projectResource) switch
            {
                (ERequestType.ProjectResourceAssignment, _) => ERequestState.PendingForPIAccept,
                _ => ERequestState.Pending,
            },
            ProjectResource = projectResource,
            ProjectResourceName = projectResourceName,
            Provider = provider,
            ContactEmail = contactEmail
        };
        await _uow.RequestRepository.InsertAsync(newRequest, ct);
        var changed = await _uow.SaveAsync(ct);

        if (changed > 0)
        {
            await SendRequestCreatedEmailToUser(newRequest, ct);
            await SendRequestCreatedEmailToProjectOwner(newRequest, ct);

            await _actionLinkService.GenerateActionLinks(baseUri, newRequest.Id, ct);
            await SendRequestCreatedEmailToPI(newRequest, _actionValidHours, ct);
        }
    }

    public async Task DeclineAssignRequestAsync(Guid requestId, string? reason, CancellationToken ct)
    {
        var request = await _uow.RequestRepository.GetByIdAsync(requestId, ct, [r => r.User, r => r.Project]);

        RequestException.ThrowIfNull(request);

        if (request.State is ERequestState.Accepted or ERequestState.Rejected)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrEmpty(request.User.Email);

        request.State = ERequestState.Rejected;
        var changes = await _uow.SaveAsync(ct);
        DatabaseCommitException.ThrowIfLessThan(1, changes);

        await SendRejectEmailToUser(request, ct);
    }

    public async Task<List<RequestModel>> GetRequestsAsync(string? projectMangerSid, ERequestState? state, ProjectShortName? projectShortName = null, ERequestType? type = null, CancellationToken ct = default)
    {
        var requests = await _uow.RequestRepository.GetByStateAndPm(state, projectShortName, projectMangerSid, type, ct)
            ;
        return requests.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<List<RequestModel>> GetSentRequestsAsync(string idpUserId, ERequestState? state, ProjectShortName? projectShortName = null, ERequestType? type = null, CancellationToken ct = default)
    {
        var requests = await _uow.RequestRepository.GetForUser(state, projectShortName, idpUserId, type, ct)
            ;
        return requests.Select(s => s.ConvertToModel()).ToList();
    }

    public async Task<PublicRequestModel> GetPublicRequestInfoAsync(Guid requestId, CancellationToken ct = default)
    {
        var request = await _uow.RequestRepository.GetByIdForPublicEndpoint(requestId, ct)
            ;

        return request.ConvertToPublicModel();
    }
}