using Core.Domain;
using Core.Features.Project.CreateProject;
using Core.Shared.OutputCache;
using Core.UnitTests.Shared;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.Projects;

/// <summary>
/// Characterization tests pinning every guard of <see cref="CreateProjectEndpoint.HandleAsync"/>, its order and
/// its status and code, before the guards are extracted into private methods (T-197-03).
/// </summary>
public sealed class CreateProjectEndpointCharacterizationTests
{
    private sealed class Fixture
    {
        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Repository { get; } = new();
        public Mock<IRepository<Domain.User.User, IdpId>> Users { get; } = new();
        public Mock<IUserDirectory> Directory { get; } = new();
        public Mock<IProjectDomainTypeLookup> DomainTypes { get; } = new();
        public Mock<IRoleListService> RoleList { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IOutputCacheInvalidator> Cache { get; } = new();
        public ProjectAggregate? Added { get; private set; }

        public Fixture()
        {
            Repository.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<ProjectAggregate?>(null));
            Repository.Setup(r => r.AddAsync(It.IsAny<ProjectAggregate>(), It.IsAny<CancellationToken>()))
                .Returns((ProjectAggregate p, CancellationToken _) =>
                {
                    Added = p;
                    return ValueTask.FromResult<ProjectAggregate?>(p);
                });
            Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.FromResult<Domain.User.User?>(Domain.User.User.CreateUser(
                    Email.Create("u@example.com"), UserName.Create("creator"), IdpId.Create("creator-idp"))));
            Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(UserId.New());
            DomainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProjectDomainType.FromName(DomainTypeName.Create("HPC")));
            RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(Roles()));
            UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            Cache.Setup(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(ValueTask.CompletedTask);
        }

        public Task<IResult> InvokeAsync(CreateProjectEndpoint.Request request) =>
            CreateProjectEndpoint.HandleAsync(
                request, Caller(), Repository.Object, Users.Object, Directory.Object, DomainTypes.Object,
                RoleList.Object, Options.Create(new WellKnownRoles()), UnitOfWork.Object, Cache.Object, new RecordingAuditLogger<CreateProjectEndpoint.AuditCategory>(), NullLogger<CreateProjectEndpoint.AuditCategory>.Instance, new DefaultHttpContext(), CancellationToken.None);

        public void VerifyNothingWritten()
        {
            Repository.Verify(r => r.AddAsync(It.IsAny<ProjectAggregate>(), It.IsAny<CancellationToken>()), Times.Never);
            UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            Cache.Verify(c => c.EvictAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private static IList<RoleInfo> Roles() =>
    [
        new RoleInfo(4, "Owner", "Owner", "Project", [], true, 20),
        new RoleInfo(3, "Member", "Member", "Project", [], true, 10),
    ];

    private static Caller Caller() => new(
        new IdpId("u"), "User", null, new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static CreateProjectEndpoint.Request ValidRequest() => new(
        ShortName: "PROJ-1", Name: "Project One", Description: null, ContactEmail: null,
        StartDate: DateTimeOffset.UtcNow, EndDate: DateTimeOffset.UtcNow.AddDays(30), DomainTypeName: "HPC");

    private static void AssertProblem(IResult result, int status, string code)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
    }

    private static void AssertProblem(IResult result, int status, string code, string detail)
    {
        AssertProblem(result, status, code);
        Assert.Equal(detail, Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Should_ReturnShortNameInvalid_When_ShortNameUnparseable(string shortName)
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { ShortName = shortName, Name = "" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.ShortNameInvalid);
        fixture.Directory.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnNameInvalid_Before_DescriptionCheck()
    {
        var result = await new Fixture().InvokeAsync(ValidRequest() with { Name = "", Description = new string('d', 501) });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.NameInvalid);
    }

    [Fact]
    public async Task Should_ReturnDescriptionTooLong_When_DescriptionExceeds500Characters()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Description = new string('d', 501), ContactEmail = "not-an-email" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.DescriptionTooLong);
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_AcceptDescriptionOfExactly500Characters()
    {
        var fixture = new Fixture();
        var description = new string('d', 500);

        var result = await fixture.InvokeAsync(ValidRequest() with { Description = description });

        Assert.IsType<Created<CreateProjectEndpoint.Response>>(result);
        Assert.Equal(description, fixture.Added!.Description!.Value.Value);
    }

    [Fact]
    public async Task Should_ReturnContactEmailInvalid_When_EmailUnparseable()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { ContactEmail = "not-an-email", DomainTypeName = "" });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.ContactEmailInvalid);
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnDomainTypeNameInvalid_Before_DateCheck()
    {
        var request = ValidRequest() with { DomainTypeName = "   ", EndDate = DateTimeOffset.UtcNow.AddDays(-1) };

        var result = await new Fixture().InvokeAsync(request);

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.DomainTypeNameInvalid);
    }

    [Fact]
    public async Task Should_ReturnEndDateBeforeStartDate_Before_AnyLookup()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { EndDate = DateTimeOffset.UtcNow.AddDays(-1) });

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.EndDateBeforeStartDate);
        fixture.Directory.VerifyNoOtherCalls();
        fixture.Users.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_TreatWhitespaceDescriptionAndEmailAsAbsent()
    {
        var fixture = new Fixture();

        var result = await fixture.InvokeAsync(ValidRequest() with { Description = "   ", ContactEmail = "  " });

        Assert.IsType<Created<CreateProjectEndpoint.Response>>(result);
        Assert.Null(fixture.Added!.Description);
        Assert.Null(fixture.Added.Email);
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_CallerIsNotInTheDirectory()
    {
        var fixture = new Fixture();
        fixture.Directory.Setup(d => d.FindUserIdAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserId?)null);

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned");
        fixture.Users.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnCallerNotProvisioned_When_FounderUserIsMissing()
    {
        var fixture = new Fixture();
        fixture.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(null));

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status404NotFound, "caller.not_provisioned");
        fixture.DomainTypes.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnDomainTypeNotFound_Before_RoleLookup()
    {
        var fixture = new Fixture();
        fixture.DomainTypes.Setup(d => d.FindByNameAsync(It.IsAny<DomainTypeName>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProjectDomainType?)null);

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status400BadRequest, ProjectErrorCodes.DomainTypeNotFound);
        fixture.RoleList.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_RoleServiceFails()
    {
        var fixture = new Fixture();
        fixture.RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Failure(new Error("RoleServiceDown", ErrorSeverity.Error, "down")));

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Failed to retrieve roles.");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnUpstream_When_OwnerRoleIsNotInTheCatalog()
    {
        var fixture = new Fixture();
        fixture.RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success([new RoleInfo(3, "Member", "Member", "Project", [], true, 10)]));

        var result = await fixture.InvokeAsync(ValidRequest());

        AssertProblem(result, StatusCodes.Status502BadGateway, "upstream.role_service_unavailable", "Project owner role is not configured.");
        fixture.Repository.VerifyNoOtherCalls();
        fixture.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_MatchOwnerRoleNameIgnoringCase()
    {
        var fixture = new Fixture();
        var founder = Domain.User.User.CreateUser(Email.Create("u@example.com"), UserName.Create("creator"), IdpId.Create("creator-idp"));
        fixture.Users.Setup(u => u.FindAsync(It.IsAny<IdpId>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<Domain.User.User?>(founder));
        fixture.RoleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(
                [new RoleInfo(3, "MEMBER", "Member", "Project", [], true, 10), new RoleInfo(4, "OWNER", "Owner", "Project", [], true, 20)]));

        var result = await fixture.InvokeAsync(ValidRequest());

        Assert.IsType<Created<CreateProjectEndpoint.Response>>(result);
        Assert.Equal(ProjectRoleId.Create(4), fixture.Added!.ActiveRoleOf(founder));
    }

    [Fact]
    public async Task Should_PersistParsedFieldsAndEvictCallerUserInfo_When_Valid()
    {
        var fixture = new Fixture();
        var request = ValidRequest() with { Description = "about", ContactEmail = "owner@example.com" };

        var result = await fixture.InvokeAsync(request);

        var created = Assert.IsType<Created<CreateProjectEndpoint.Response>>(result);
        Assert.Equal("v2/Projects/PROJ-1", created.Location);
        Assert.Equal("PROJ-1", created.Value!.ShortName);
        var project = fixture.Added!;
        Assert.Equal("PROJ-1", project.ShortName.Value);
        Assert.Equal("Project One", project.Name.Value);
        Assert.Equal("about", project.Description!.Value.Value);
        Assert.Equal("owner@example.com", project.Email!.Value.Value);
        Assert.Equal(request.StartDate, project.StartDate);
        Assert.Equal(request.EndDate, project.EndDate);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Cache.Verify(c => c.EvictAsync(CacheTags.UserInfo("u"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
