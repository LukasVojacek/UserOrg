using Core.Domain.Shared;
using Core.Features.Project.UpdateProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// Characterization tests pinning every guard of <see cref="UpdateProjectEndpoint.HandleAsync"/>, its order and
/// its status and code, before the guards are extracted into private methods (T-197-03).
/// </summary>
public sealed class UpdateProjectEndpointCharacterizationTests
{
    private static readonly ProjectShortName ShortName = ProjectShortName.Create("PROJ-1");

    private sealed class Fixture
    {
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Repository { get; } = new();
        public Mock<IProjectDomainTypeLookup> DomainTypes { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();
        public ProjectAggregate Project { get; } = BuildProject();

        public Fixture()
        {
            Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult<ProjectAggregate?>(Project));
            DomainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProjectDomainType.FromName(DomainTypeName.Create("Engineering")));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync(UpdateProjectEndpoint.Request request, Caller? caller = null) =>
            UpdateProjectEndpoint.HandleAsync(
                ShortName, request, caller ?? InScopeCaller(), Repository.Object, DomainTypes.Object,
                UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<UpdateProjectEndpoint.AuditCategory>(), NullLogger<UpdateProjectEndpoint.AuditCategory>.Instance, CancellationToken.None);

        public void VerifyNothingWritten()
        {
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private static ProjectAggregate BuildProject()
    {
        var project = ProjectAggregate.Create(
            ShortName,
            ProjectName.Create("Project One"),
            ProjectDomainType.FromName(DomainTypeName.Create("HPC")),
            UserId.New(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            EOperationMode.Editable,
            Description.Create("original"),
            Email.Create("original@example.com"));
        project.ClearDomainEvents();
        return project;
    }

    private static Caller InScopeCaller() => new(
        new IdpId("u"), "ProjectManager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["ProjectManager"] = new HashSet<ProjectShortName> { ShortName } });

    private static Caller OutOfScopeCaller() => new(
        new IdpId("u"), "ProjectManager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["ProjectManager"] = new HashSet<ProjectShortName> { ProjectShortName.Create("OTHER-1") } });

    private static UpdateProjectEndpoint.Request ValidRequest() => new(
        Name: "Renamed",
        Description: "updated",
        ContactEmail: "owner@example.com",
        StartDate: DateTimeOffset.UtcNow,
        EndDate: DateTimeOffset.UtcNow.AddDays(60),
        DomainTypeName: "Engineering");

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_Before_Validation_When_CallerOutOfScope()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Name = "" }, OutOfScopeCaller());

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnNameInvalid_When_NameExceeds100Characters()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Name = new string('n', 101), Description = new string('d', 501) });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.NameInvalid);
        fixture.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_AcceptNameOfExactly100Characters()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Name = new string('n', 100) });

        Assert.IsType<NoContent>(result);
        Assert.Equal(new string('n', 100), fixture.Project.Name.Value);
    }

    [Fact]
    public async Task Should_ReturnDescriptionTooLong_Before_EmailCheck()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Description = new string('d', 501), ContactEmail = "not-an-email" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.DescriptionTooLong);
        fixture.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnContactEmailInvalid_Before_DomainTypeNameCheck()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { ContactEmail = "not-an-email", DomainTypeName = "" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.ContactEmailInvalid);
        fixture.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnDomainTypeNameInvalid_Before_ProjectLookup()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { DomainTypeName = "" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.DomainTypeNameInvalid);
        fixture.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_Before_DomainTypeLookup()
    {
        var fixture = new Fixture();
        fixture.Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(null));

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found");
        fixture.DomainTypes.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnEndDateBeforeStartDateWithoutSaving_When_RescheduleFails()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { EndDate = DateTimeOffset.UtcNow.AddDays(-1) });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.EndDateBeforeStartDate);
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ClearDescriptionAndEmail_When_RequestSendsWhitespace()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Description = " ", ContactEmail = null });

        Assert.IsType<NoContent>(result);
        Assert.Null(fixture.Project.Description);
        Assert.Null(fixture.Project.Email);
    }

    [Fact]
    public async Task Should_ApplyEveryFieldSaveAndEvictProjectTag_When_Valid()
    {
        var fixture = new Fixture();
        var request = ValidRequest();

        var result = await fixture.InvokeAsync(request);

        Assert.IsType<NoContent>(result);
        var project = fixture.Project;
        Assert.Equal("Renamed", project.Name.Value);
        Assert.Equal("updated", project.Description!.Value.Value);
        Assert.Equal("owner@example.com", project.Email!.Value.Value);
        Assert.Equal(request.StartDate, project.StartDate);
        Assert.Equal(request.EndDate, project.EndDate);
        Assert.Equal("Engineering", project.DomainType.Value);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Cache.Verify(c => c.EvictAsync(CacheTags.Project("PROJ-1"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
