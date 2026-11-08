using Forge.Core.Features.Role.Services;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.IRepositories;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

using ActionLinkConfig = UserOrganizationManagement.Domain.Configuration.ActionLink;
using MailConfig = UserOrganizationManagement.Domain.Configuration.MailConfiguration;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Issue #278: the ProjectResourceAssignment accept path writes the assignment and the request state in
/// two saves. These run against SQLite because EF InMemory silently ignores transactions, so only a real
/// relational provider can prove a rollback.
/// </summary>
public sealed class ProjectRequestServiceTransactionTests : IDisposable
{
    private const string ResourceName = "VLQ-LEXIS-TEST-P0001";
    private const string AcceptLink = "aabbccdd";
    private static readonly ProjectShortName _shortName = ProjectShortName.Create("vlq_demo_project");

    private readonly SqliteConnection _connection;
    private readonly DatabaseContext _dbContext;
    private readonly Request _request;
    private readonly Project _project;
    private readonly ProjectResource _resource;

    public ProjectRequestServiceTransactionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbContext = CreateDbContext();
        _dbContext.Database.EnsureCreated();

        _project = new Project
        {
            ShortName = _shortName,
            Name = "VLQ demo",
            Email = "pi@vsb.cz",
            ProjectDomainType = new ProjectDomainType { Name = "Science" },
            CreatedByUser = new User { Email = "owner@vsb.cz", UserName = "owner", IdentityProviderUserId = "idp-owner" }
        };
        _resource = new ProjectResource
        {
            Id = Guid.CreateVersion7(),
            Name = ResourceName,
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddYears(1)
        };
        _request = new Request
        {
            Type = ERequestType.ProjectResourceAssignment,
            State = ERequestState.PendingForPIAccept,
            Project = _project,
            ProjectResource = _resource,
            ProjectResourceName = ResourceName,
            User = new User { Email = "jan.swiatkowski@vsb.cz", UserName = "jan", IdentityProviderUserId = "idp-jan" }
        };
        _dbContext.Add(_request);
        _dbContext.Add(new ActionLink
        {
            Action = EActionType.Accept,
            Link = AcceptLink,
            Request = _request,
            ValidTo = DateTime.UtcNow.AddHours(1)
        });
        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Accept_StateSaveFails_LeavesResourceUnassigned()
    {
        var sut = CreateSut(stateSaveFails: true);

        await Assert.ThrowsAsync<DbUpdateException>(() => sut.AcceptAssignRequestAsync(_request.Id, CancellationToken.None));

        using var reader = CreateDbContext();
        Assert.Empty(reader.AssignedProjectResourceToProjects);
        Assert.Equal(ERequestState.PendingForPIAccept, reader.SelfOnboardingRequests.Single().State);
    }

    // The sends run after the commit: a mail-server outage must not undo an accept the assignment has
    // already granted. The reader shares the connection, so it would also see an accept still sitting in
    // an open transaction - hence the CurrentTransaction assertion, which is what tells "committed" from
    // "not yet rolled back". Moving SendAcceptedEmailToUser above CommitAsync makes this test fail.
    [Fact]
    public async Task Accept_EmailSendFails_DoesNotRollBackTheAccept()
    {
        var sut = CreateSut(emailSendFails: true);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.AcceptAssignRequestAsync(_request.Id, CancellationToken.None));

        Assert.Null(_dbContext.Database.CurrentTransaction);
        using var reader = CreateDbContext();
        Assert.False(reader.AssignedProjectResourceToProjects.Single().IsDeleted);
        Assert.Equal(ERequestState.Accepted, reader.SelfOnboardingRequests.Single().State);
    }

    // Revised semantics (ADR-0064 said "invalidated on use"): the burn now rides the accept's
    // transaction, so a rolled-back accept leaves the PI's link usable instead of stranding them.
    [Fact]
    public async Task HandleActionLink_AcceptRollsBack_LeavesLinkUnburned()
    {
        var sut = CreateSut(stateSaveFails: true);
        var actionLinkService = CreateActionLinkService(stateSaveFails: true);

        await Assert.ThrowsAsync<DbUpdateException>(() => actionLinkService.Handle(AcceptLink, sut, CancellationToken.None));

        using var reader = CreateDbContext();
        Assert.False(reader.Set<ActionLink>().Single().IsDeleted);
    }

    private DatabaseContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<DatabaseContext>().UseSqlite(_connection).Options);

    // A unit of work of its own, over the same context - production registers both services scoped, so
    // they share one context and one change tracker.
    private ActionLinkService CreateActionLinkService(bool stateSaveFails) =>
        new(Options.Create(new ActionLinkConfig()), BuildUnitOfWork(stateSaveFails).Object, new Mock<IRequestRepository>().Object);

    private ProjectRequestService CreateSut(bool stateSaveFails = false, bool emailSendFails = false)
    {
        var uow = BuildUnitOfWork(stateSaveFails);

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(emailSendFails ? new FailingHandler() : new OkHandler()) { BaseAddress = new Uri("http://localhost/") });

        return new ProjectRequestService(
            new Mock<ILogger<ProjectRequestService>>().Object,
            new Mock<IActionLinkService>().Object,
            uow.Object,
            httpFactory.Object,
            Options.Create(new ActionLinkConfig()),
            Options.Create(new MailConfig { SenderEmail = "noreply@lexis", PortalAddress = "http://portal" }),
            BuildProjectService(uow).Object,
            new Mock<IProviderRepository>().Object,
            new Mock<IProjectResourceRepository>().Object,
            new Mock<IRoleListService>().Object,
            _dbContext);
    }

    /// <summary>
    /// Saves through the same <see cref="DatabaseContext"/> the service transacts on - the production
    /// wiring registers both the unit of work and <see cref="IUserOrgDbContext"/> as scoped over one
    /// instance. <paramref name="stateSaveFails"/> breaks the save that carries the accepted state, the
    /// second of the two writes the transaction has to keep together.
    /// </summary>
    private Mock<IUserOrgUnitOfWork> BuildUnitOfWork(bool stateSaveFails)
    {
        var uow = new Mock<IUserOrgUnitOfWork>();
        uow.Setup(u => u.RequestRepository.GetByIdWithUserAndProjectUser(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_request);
        uow.Setup(u => u.ActionLinkRepository).Returns(new ActionLinkRepository(_dbContext));
        uow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => stateSaveFails && _request.State == ERequestState.Accepted
                ? throw new DbUpdateException("state save failed")
                : _dbContext.SaveChangesAsync(ct));
        return uow;
    }

    /// <summary>
    /// Stands in for <see cref="ProjectService.AssignedProjectResourceToProjectAsync"/>: it writes the
    /// assignment and saves it through the unit of work, which is exactly the write the accept's second
    /// save must be atomic with.
    /// </summary>
    /// <remarks>
    /// The double is faithful on the one property the fix rests on. <c>AddDbContext&lt;IUserOrgDbContext,
    /// DatabaseContext&gt;</c> (DataAccessExtensions.cs) registers a forwarding descriptor, so the
    /// <c>DatabaseContext</c> injected into <c>UserOrgUnitOfWork</c> and the <c>IUserOrgDbContext</c>
    /// injected into <see cref="ProjectRequestService"/> are one scoped instance - the real
    /// <c>ProjectService</c>, which saves through that same unit of work, therefore enlists in the ambient
    /// transaction exactly as this double does.
    /// </remarks>
    private Mock<IProjectService> BuildProjectService(Mock<IUserOrgUnitOfWork> uow)
    {
        var projectService = new Mock<IProjectService>();
        projectService
            .Setup(s => s.AssignedProjectResourceToProjectAsync(
                It.IsAny<ProjectShortName>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProjectShortName _, string _, Guid? _, CancellationToken ct) =>
            {
                _project.AssignedProjectResources.Add(new AssignedProjectResourceToProject { Project = _project, ProjectResource = _resource });
                await uow.Object.SaveAsync(ct);
            });
        return projectService;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("mail server unreachable");
    }
}
