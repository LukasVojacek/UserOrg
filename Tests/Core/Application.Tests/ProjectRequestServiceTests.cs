using FluentValidation;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;

using Core.Domain.Exceptions;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Exceptions;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;

using ActionLinkConfig = UserOrganizationManagement.Domain.Configuration.ActionLink;
using MailConfig = UserOrganizationManagement.Domain.Configuration.MailConfiguration;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ProjectRequestServiceTests
{
    private const string ResourceName = "VLQ-LEXIS-TEST-P0001";
    private static readonly ProjectShortName _shortName = ProjectShortName.Create("vlq_demo_project");

    // Issue #211: accepting a ProjectResourceAssignment with an existing resource in
    // PendingForPIAccept used to hit no branch -> 0 changes -> 500 "changes was less then 1".
    [Fact]
    public async Task Accept_ResourceExists_PendingPI_AssignsResourceAndAccepts()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: true);
        var (sut, _, projectService, _) = CreateSut(request);

        await sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        projectService.Verify(
            s => s.AssignedProjectResourceToProjectAsync(_shortName, ResourceName, null, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(ERequestState.Accepted, request.State);
    }

    // Resource not yet in the system: PI accept parks the request for the HPC side.
    [Fact]
    public async Task Accept_ResourceNull_PendingPI_MovesToPendingForHpc()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: false);
        var (sut, _, projectService, _) = CreateSut(request);

        await sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ERequestState.PendingForHPCProject, request.State);
        projectService.Verify(
            s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Resource was created after the PI approved: re-accept assigns it and finalizes.
    [Fact]
    public async Task Accept_ResourceExists_PendingHpc_AssignsResourceAndAccepts()
    {
        var request = BuildResourceRequest(ERequestState.PendingForHPCProject, withResource: true);
        var (sut, _, projectService, _) = CreateSut(request);

        await sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        projectService.Verify(
            s => s.AssignedProjectResourceToProjectAsync(_shortName, ResourceName, null, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(ERequestState.Accepted, request.State);
    }

    [Fact]
    public async Task Accept_SelfOnboarding_AssignsUserAndAccepts()
    {
        var request = BuildSelfOnboardingRequest();
        var assignedBy = Guid.NewGuid();
        var (sut, _, projectService, _) = CreateSut(request);

        await sut.AcceptAssignRequestAsync(request.Id, assignedBy, CancellationToken.None);

        projectService.Verify(
            s => s.AssignedUserToProjectWithRoleAsync(
                _shortName, request.User!.IdentityProviderUserId, assignedBy,
                RoleInfoConstants.ProjectMemberRoleName, It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(ERequestState.Accepted, request.State);
    }

    // Consolidation reuses ProjectService.AssignedProjectResourceToProjectAsync, so its validations now
    // apply to the accept flow (UI and e-mail). A failed assignment must propagate and leave the request
    // un-accepted (assign runs before the state change).
    [Fact]
    public async Task Accept_ResourceExists_ReadOnlyProject_PropagatesAndDoesNotAccept()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: true);
        var (sut, _, projectService, _) = CreateSut(request);
        projectService
            .Setup(s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RecordReadOnlyException());

        await Assert.ThrowsAsync<RecordReadOnlyException>(
            () => sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ERequestState.PendingForPIAccept, request.State);
    }

    [Fact]
    public async Task Accept_ResourceExists_ProjectHasNonSharedResource_PropagatesAndDoesNotAccept()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: true);
        var (sut, _, projectService, _) = CreateSut(request);
        projectService
            .Setup(s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProjectAlreadyHasAssignedNonSharedResourceException());

        await Assert.ThrowsAsync<ProjectAlreadyHasAssignedNonSharedResourceException>(
            () => sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(ERequestState.PendingForPIAccept, request.State);
    }

    // Issue #212: after a resource is unassigned (soft delete), the create-request guard counted the
    // soft-deleted assignment row and refused a new ProjectResourceAssignment request. The guard must
    // ignore soft-deleted rows (mirroring the sibling SelfOnboarding guard) so the resource can be
    // re-requested and later reactivated on accept.
    [Fact]
    public async Task AddAssignRequest_ResourceAssignmentSoftDeleted_CreatesRequest()
    {
        var project = BuildProjectWithResourceAssignment(isDeleted: true);
        var (sut, requestRepo) = CreateAddSut(project);

        await sut.AddAssignRequestAsync(
            _shortName, "idp-jan", ERequestType.ProjectResourceAssignment, ResourceName,
            null, null, new HostString("localhost"), CancellationToken.None);

        requestRepo.Verify(
            r => r.InsertAsync(
                It.Is<Request>(req => req.State == ERequestState.PendingForPIAccept && req.ProjectResourceName == ResourceName),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Counterpart: an active (not soft-deleted) assignment must still be rejected.
    [Fact]
    public async Task AddAssignRequest_ResourceAssignmentActive_Throws()
    {
        var project = BuildProjectWithResourceAssignment(isDeleted: false);
        var (sut, requestRepo) = CreateAddSut(project);

        await Assert.ThrowsAsync<ValidationException>(() => sut.AddAssignRequestAsync(
            _shortName, "idp-jan", ERequestType.ProjectResourceAssignment, ResourceName,
            null, null, new HostString("localhost"), CancellationToken.None));

        requestRepo.Verify(r => r.InsertAsync(It.IsAny<Request>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Criterion 7a — the SelfOnboarding narrowing. AssignedUserToProjectWithRoleAsync throws for an
    // already-active member before it ever writes the role, so a first-ever request from such a member
    // must keep failing instead of being accepted with no role. Modelled as: active membership present,
    // permission never written.
    [Fact]
    public async Task Accept_SelfOnboarding_ActiveMemberWithoutPermission_PropagatesAndDoesNotAccept()
    {
        var request = BuildSelfOnboardingRequest();
        AddActiveMembership(request);
        var (sut, _, projectService, _) = CreateSut(request);
        ThrowAlreadyExistOnUserAssignment(projectService, request);

        await Assert.ThrowsAsync<RecordAlreadyExistException>(
            () => sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(ERequestState.Accepted, request.State);
    }

    // Criterion 7a, second shape: ProjectService inserts the permission row before it assigns the role,
    // so a row with no SystemRoleId is a membership whose role was never written — the same no-role
    // acceptance the narrowing exists to prevent.
    [Fact]
    public async Task Accept_SelfOnboarding_PermissionWithoutRole_PropagatesAndDoesNotAccept()
    {
        var request = BuildSelfOnboardingRequest();
        AddActiveMembership(request);
        AddPermission(request, systemRoleId: null);
        var (sut, _, projectService, _) = CreateSut(request);
        ThrowAlreadyExistOnUserAssignment(projectService, request);

        await Assert.ThrowsAsync<RecordAlreadyExistException>(
            () => sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        Assert.NotEqual(ERequestState.Accepted, request.State);
    }

    // Criteria 6/7/7b, SelfOnboarding branch: a retry after a split-brain (assignment committed, the
    // Request.State save failed) finds the membership and the role in place, so the retry only
    // has to finish the state change — and leaves a trace of the swallowed exception.
    [Fact]
    public async Task Accept_SelfOnboarding_AssignmentAlreadyCommitted_AcceptsAndLogsWarning()
    {
        var request = BuildSelfOnboardingRequest();
        AddActiveMembership(request);
        AddPermission(request, systemRoleId: 3);
        var (sut, _, projectService, logger) = CreateSut(request);
        ThrowAlreadyExistOnUserAssignment(projectService, request);

        await sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ERequestState.Accepted, request.State);
        VerifyWarningLogged(logger, request, Times.Once());
    }

    // Criteria 6/7/7b, resource branch: the exception itself proves the resource is already assigned,
    // so the retry accepts the request without re-assigning.
    [Fact]
    public async Task Accept_ResourceExists_AssignmentAlreadyCommitted_AcceptsAndLogsWarning()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: true);
        var (sut, _, projectService, logger) = CreateSut(request);
        projectService
            .Setup(s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(AlreadyAssigned(request));

        await sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ERequestState.Accepted, request.State);
        VerifyWarningLogged(logger, request, Times.Once());
    }

    // Issue #278: a failed commit leaves the transaction aborted, so the rollback that follows throws
    // too. Rolling back unguarded - and on the request's token, which may already be cancelled - would
    // replace the real database failure with a misleading one and skip the change-tracker cleanup.
    [Fact]
    public async Task Accept_CommitAndRollbackBothFail_PropagatesCommitFailureAndClearsChangeTracker()
    {
        var request = BuildResourceRequest(ERequestState.PendingForPIAccept, withResource: true);
        var transaction = new Mock<IDbContextTransaction>();
        transaction.Setup(t => t.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("commit failed"));
        transaction.Setup(t => t.RollbackAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("This SqlTransaction has completed"));
        var dbContext = BuildDbContext(transaction);
        var (sut, _, _, _) = CreateSut(request, dbContext);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => sut.AcceptAssignRequestAsync(request.Id, Guid.NewGuid(), CancellationToken.None));

        transaction.Verify(t => t.RollbackAsync(CancellationToken.None), Times.Once);
        dbContext.Verify(c => c.ClearChangeTracker(), Times.Once);
    }

    private static void ThrowAlreadyExistOnUserAssignment(Mock<IProjectService> projectService, Request request) =>
        projectService
            .Setup(s => s.AssignedUserToProjectWithRoleAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(AlreadyAssigned(request));

    // Mirrors the message ProjectService raises: it embeds the IdP subject id and the project short name,
    // the values a Warning record must not carry (ADR-0006).
    private static RecordAlreadyExistException AlreadyAssigned(Request request) =>
        new($"""User "{request.User!.IdentityProviderUserId}" is already associated with Project "{_shortName.Value}"!""");

    private static void AddActiveMembership(Request request) =>
        request.Project!.AssignedUsers.Add(new AssignedUserToProject { Project = request.Project, User = request.User });

    private static void AddPermission(Request request, int? systemRoleId) =>
        request.Project!.UserPermissions.Add(new UserPermission { Project = request.Project, User = request.User, SystemRoleId = systemRoleId });

    // The swallowed exception is named in the record but never carried, so its message - which embeds the
    // IdP subject id and the project short name - stays out of the log (ADR-0006).
    private static void VerifyWarningLogged(Mock<ILogger<ProjectRequestService>> logger, Request request, Times times) =>
        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => IsSwallowRecordWithoutPii(state.ToString()!, request)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private static bool IsSwallowRecordWithoutPii(string record, Request request) =>
        record.Contains(nameof(RecordAlreadyExistException), StringComparison.Ordinal)
        && record.Contains(request.Id.ToString(), StringComparison.Ordinal)
        && !record.Contains(request.User!.IdentityProviderUserId, StringComparison.Ordinal)
        && !record.Contains(_shortName.Value, StringComparison.Ordinal);

    private static Project BuildProjectWithResourceAssignment(bool isDeleted)
    {
        var project = new Project
        {
            Id = ProjectId.Create(Guid.NewGuid()),
            ShortName = _shortName,
            Email = "pi@vsb.cz",
            CreatedByUser = new User { Email = "owner@vsb.cz", UserName = "owner" }
        };
        project.AssignedProjectResources.Add(new AssignedProjectResourceToProject
        {
            ProjectResource = new ProjectResource { Id = Guid.NewGuid(), Name = ResourceName },
            IsDeleted = isDeleted
        });
        return project;
    }

    private static (ProjectRequestService Service, Mock<IRequestRepository> RequestRepository) CreateAddSut(Project project)
    {
        var user = new User { Email = "jan.swiatkowski@vsb.cz", UserName = "jan", IdentityProviderUserId = "idp-jan" };

        var requestRepo = new Mock<IRequestRepository>();
        requestRepo.Setup(r => r.GetRequestsForUser(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Request>());

        var uow = new Mock<IUserOrgUnitOfWork>();
        uow.Setup(u => u.ProjectRepository.GetProjectByShortNameWithCreatedUsersAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);
        uow.Setup(u => u.UserRepository.GetBySidWithProjectsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        uow.Setup(u => u.RequestRepository).Returns(requestRepo.Object);
        uow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var resourceRepo = new Mock<IProjectResourceRepository>();
        resourceRepo.Setup(r => r.GetByNameWithIncludesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectResource { Id = Guid.NewGuid(), Name = ResourceName });

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new OkHandler()) { BaseAddress = new Uri("http://localhost/") });

        var service = new ProjectRequestService(
            new Mock<ILogger<ProjectRequestService>>().Object,
            new Mock<IActionLinkService>().Object,
            uow.Object,
            httpFactory.Object,
            Options.Create(new ActionLinkConfig()),
            Options.Create(new MailConfig { SenderEmail = "noreply@lexis", PortalAddress = "http://portal" }),
            new Mock<IProjectService>().Object,
            new Mock<IProviderRepository>().Object,
            resourceRepo.Object,
            new Mock<IRoleListService>().Object,
            new Mock<IUserOrgDbContext>().Object);

        return (service, requestRepo);
    }

    private static Request BuildResourceRequest(ERequestState state, bool withResource)
    {
        var request = NewRequest(ERequestType.ProjectResourceAssignment, state);
        request.ProjectResourceName = ResourceName;
        request.Provider = new Provider { SupportEmail = "hpc-support@it4i.cz" };
        request.ProjectResource = withResource
            ? new ProjectResource { Id = Guid.NewGuid(), Name = ResourceName }
            : null;
        return request;
    }

    private static Request BuildSelfOnboardingRequest()
        => NewRequest(ERequestType.SelfOnboarding, ERequestState.Pending);

    private static Request NewRequest(ERequestType type, ERequestState state) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        State = state,
        Project = new Project
        {
            Id = ProjectId.Create(Guid.NewGuid()),
            ShortName = _shortName,
            Email = "pi@vsb.cz",
            CreatedByUser = new User { Email = "owner@vsb.cz", UserName = "owner" }
        },
        User = new User { Email = "jan.swiatkowski@vsb.cz", UserName = "jan", IdentityProviderUserId = "idp-jan" }
    };

    private static (ProjectRequestService Service, Mock<IUserOrgUnitOfWork> Uow, Mock<IProjectService> ProjectService, Mock<ILogger<ProjectRequestService>> Logger)
        CreateSut(Request request, Mock<IUserOrgDbContext>? dbContext = null)
    {
        // Source-generated LoggerMessage checks IsEnabled before formatting; a loose mock would
        // return false and no log record would ever reach the mock.
        var logger = new Mock<ILogger<ProjectRequestService>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var uow = new Mock<IUserOrgUnitOfWork>();
        uow.Setup(u => u.RequestRepository.GetByIdWithUserAndProjectUser(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(request);
        uow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var projectService = new Mock<IProjectService>();
        projectService
            .Setup(s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        projectService
            .Setup(s => s.AssignedUserToProjectWithRoleAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
                new List<RoleInfo> { new(3, RoleInfoConstants.ProjectMemberRoleName, "Member", "Project", [], true, 1) }));

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(new OkHandler()) { BaseAddress = new Uri("http://localhost/") });

        var service = new ProjectRequestService(
            logger.Object,
            new Mock<IActionLinkService>().Object,
            uow.Object,
            httpFactory.Object,
            Options.Create(new ActionLinkConfig()),
            Options.Create(new MailConfig { SenderEmail = "noreply@lexis", PortalAddress = "http://portal" }),
            projectService.Object,
            new Mock<IProviderRepository>().Object,
            new Mock<IProjectResourceRepository>().Object,
            roleList.Object,
            (dbContext ?? BuildDbContext()).Object);

        return (service, uow, projectService, logger);
    }

    // The resource accept path opens a transaction on the context; unless a test says otherwise the
    // transaction is a no-op double and only what happens inside it is asserted.
    private static Mock<IUserOrgDbContext> BuildDbContext(Mock<IDbContextTransaction>? transaction = null)
    {
        var dbContext = new Mock<IUserOrgDbContext>();
        dbContext.Setup(c => c.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((transaction ?? new Mock<IDbContextTransaction>()).Object);
        return dbContext;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
