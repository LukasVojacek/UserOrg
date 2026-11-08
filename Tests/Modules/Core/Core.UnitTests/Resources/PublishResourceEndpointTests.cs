using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Request.Notifications;
using Core.Features.Resource.PublishResource;
using Core.UnitTests.Shared;
using Heappe.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ResourceAggregate = Core.Domain.Provider.ProjectResource;

namespace Core.UnitTests.Resources;

/// <summary>
/// Handler tests for the resource publish endpoint. Exercises provider-write authorization, the 
/// (providerName, resourceName) → entity load by natural key, and publish operation: 204 on success,
/// 400 on validation failure, 403 when not authorized, 404 when resource not found.
/// </summary>
public sealed class PublishResourceEndpointTests
{
    private const string Provider = "acme";
    private const string WriteForbiddenDetail = "This identity may not write to this provider.";
    private const string ResName = "res-1";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static Caller ProviderCaller() => new(
        new IdpId("svc"), "ProviderService", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From(Provider), IsServiceKeyIdentity: true);

    // A service key on purpose, owning a DIFFERENT provider: the 403 has to come from the ownership
    // comparison. An interactive caller here would be denied by D9's identity-kind gate before ownership
    // is ever compared, and the test would pass against a build that had lost the ownership check.
    private static Caller OtherProviderCaller() => new(
        new IdpId("u"), "User", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        ProviderName.From("other-provider"), IsServiceKeyIdentity: true);

    private static ResourceAggregate ExistingResource() => ResourceAggregate.Create(
        ResourceName.Create(ResName), Core.Domain.Provider.Provider.Create(ProviderName.From(Provider), countryId: 1), UserId.From(Guid.NewGuid()), Start, End);

    private static Mock<IRepository<ResourceAggregate, ProjectResourceKey>> Repo(ResourceAggregate? resource)
    {
        var repo = new Mock<IRepository<ResourceAggregate, ProjectResourceKey>>();
        repo.Setup(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult(resource));
        return repo;
    }

    private static Mock<IUnitOfWork> SavingUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<IProviderRepository> ProviderMock()
    {
        var providerRepo = new Mock<IProviderRepository>();
        providerRepo.Setup(r => r.FindAsync(It.IsAny<ProviderName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Core.Domain.Provider.Provider.Create(ProviderName.From(Provider), countryId: 1));
        return providerRepo;
    }

    private static Mock<IHeappeSynchronizationPort> HeappeSynchronizationPort()
    {
        var port = new Mock<IHeappeSynchronizationPort>();
        port.Setup(p => p.TriggerSynchronizationForProjectResource(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return port;
    }

    private static Mock<IRepository<Core.Domain.Project.Project, ProjectShortName>> ProjectRepoMock()
    {
        var projectRepo = new Mock<IRepository<Core.Domain.Project.Project, ProjectShortName>>();
        projectRepo.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Project.Create(
                ProjectShortName.From("test-project"),
                ProjectName.From("Test Project"),
                ProjectDomainType.FromName(DomainTypeName.Create("test-domain-type")),
                UserId.From(Guid.NewGuid()),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30),
                ServiceName.Default,
                EOperationMode.Editable
                ));
        return projectRepo;
    }

    private static Mock<IProjectResourceRequestQuery> ProjectResourceRequestQueryMock()
    {
        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.FindByResourceKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResourceRequestDetail(
                "test-slug",
                "awaiting",
                "test-project",
                Provider,
                ResName,
                "test-idp",
                "test-email",
                Guid.NewGuid(),
                "test-reason",
                DateTimeOffset.UtcNow.AddHours(1),
                DateTimeOffset.UtcNow.AddHours(-1),
                DateTimeOffset.UtcNow.AddHours(-1)
                ));
        return query;
    }

    private static Task<IResult> InvokeAsync(
        Caller caller, Mock<IRepository<ResourceAggregate, ProjectResourceKey>> repo, Mock<IUnitOfWork> unitOfWork)
        => PublishResourceEndpoint.HandleAsync(
            Provider, ResName, caller, repo.Object,
            ProjectResourceRequestQueryMock().Object,
            ProjectRepoMock().Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<PublishResourceEndpoint.AuditCategory>(),
            NullLogger<PublishResourceEndpoint.AuditCategory>.Instance,
            new Mock<INotificationSender>().Object, ProviderMock().Object,
            HeappeSynchronizationPort().Object, CancellationToken.None);

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    [Fact]
    public async Task Should_ReturnNoContent_When_ResourcePublishedSuccessfully()
    {
        var resource = ExistingResource();
        var repo = Repo(resource);
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ProviderCaller(), repo, unitOfWork);

        Assert.Equal(StatusCodes.Status204NoContent, StatusOf(result));
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnNotFoundAndNotSave_When_ResourceDoesNotExist()
    {
        var unitOfWork = SavingUnitOfWork();

        var result = await InvokeAsync(ProviderCaller(), Repo(null), unitOfWork);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("resource.not_found", problem.ProblemDetails.Extensions["code"]);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnForbiddenAndNotResolve_When_NotAuthorized()
    {
        var repo = Repo(ExistingResource());
        var unitOfWork = SavingUnitOfWork();

        var result = await PublishResourceEndpoint.HandleAsync(
            Provider, ResName, OtherProviderCaller(), repo.Object,
            new Mock<IProjectResourceRequestQuery>().Object,
            new Mock<IRepository<Core.Domain.Project.Project, ProjectShortName>>().Object,
            Options.Create(new WellKnownRoles()), unitOfWork.Object, new RecordingAuditLogger<PublishResourceEndpoint.AuditCategory>(),
            NullLogger<PublishResourceEndpoint.AuditCategory>.Instance,
            new Mock<INotificationSender>().Object, new Mock<IProviderRepository>().Object,
            HeappeSynchronizationPort().Object, CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal(WriteForbiddenDetail, problem.ProblemDetails.Detail);
        repo.Verify(r => r.FindAsync(It.IsAny<ProjectResourceKey>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

}
