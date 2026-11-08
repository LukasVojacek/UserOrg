using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.ResourceRequest;
using Core.Features.ResourceRequest.CreateResourceRequest;
using Core.Features.ResourceRequest.Flow;
using Core.Shared.Auth;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.ResourceRequests;

public sealed class CreateResourceRequestEndpointCharacterizationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProjectShortName Short = ProjectShortName.Create("proj");
    private const string PiEmail = "pi@example.org";

    private sealed class Sut
    {
        public string RouteShortName { get; set; } = Short.Value;

        public CreateResourceRequestEndpoint.Request Body { get; set; } = new("acme", "gpu", PiEmail);

        public ProjectAggregate? Project { get; set; } = ProjectAggregate.Create(
            Short, Core.Domain.Project.ProjectName.Create("Project"),
            Core.Domain.Project.ProjectDomainType.FromName(Core.Domain.Project.DomainTypeName.Create("HPC")),
            UserId.New(), Now, Now.AddYears(1), ServiceName.Default);

        public Mock<IRepository<ProjectAggregate, ProjectShortName>> Projects { get; } = new();
        public Mock<IProviderQuery> Providers { get; } = new();
        public Mock<IProjectResourceRequestQuery> Query { get; } = new();
        public Mock<IResourceRequestProjectionStore> Store { get; } = new();
        public Mock<IWorkflowClient> Workflow { get; } = new();

        public Sut()
        {
            Projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
                .Returns(() => ValueTask.FromResult(Project));
            Providers.Setup(q => q.GetProviderDetailsAsync(It.IsAny<ProviderName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProviderDetail(
                    ProviderName.From("acme"), null, Email.Create("support@acme.example"),
                    null, EOperationMode.Unspecified, ServiceName.Default, IsPrivate: false, CountryCode: "CZ", IsDeleted: false, []));
            Query.Setup(q => q.HasActiveRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Query.Setup(q => q.ListSlugsByBaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Store.Setup(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Workflow.Setup(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.CreateVersion7());
        }

        public Task<IResult> InvokeAsync(CancellationToken ct = default) =>
            CreateResourceRequestEndpoint.HandleAsync(
                RouteShortName, Body,
                new Caller(new IdpId("idp-1"), "Manager", null,
                    new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { Short } }),
                Projects.Object, Providers.Object, Query.Object, Store.Object, Workflow.Object, new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>(), NullLogger<CreateResourceRequestEndpoint.AuditCategory>.Instance, ct);

        public void VerifyNothingWritten()
        {
            Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
            Workflow.VerifyNoOtherCalls();
        }
    }

    private static void AssertProblem(IResult result, int status, string code, string? detail)
    {
        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(status, problem.StatusCode);
        Assert.Equal(code, problem.ProblemDetails.Extensions["code"]);
        if (detail is not null)
            Assert.Equal(detail, problem.ProblemDetails.Detail);
    }

    [Fact]
    public async Task Should_InsertAPendingRowWithTheRequestFields_ThenStartTheFlow_When_Valid()
    {
        var sut = new Sut();

        var result = await sut.InvokeAsync();

        var accepted = Assert.IsType<Accepted<CreateResourceRequestEndpoint.Response>>(result);
        Assert.Equal("v2/Projects/proj/ResourceRequests", accepted.Location);
        var row = (ProjectResourceRequest)sut.Store.Invocations
            .Single(i => i.Method.Name == nameof(IResourceRequestProjectionStore.InsertAsync)).Arguments[0];
        Assert.Equal(accepted.Value!.Slug, row.Slug);
        Assert.Equal("proj", row.ProjectShortName);
        Assert.Equal("acme", row.ProviderName);
        Assert.Equal("gpu", row.ResourceName);
        Assert.Equal("idp-1", row.RequesterIdpId);
        Assert.Equal(PiEmail, row.ContactEmail);
        Assert.Equal(ResourceRequestState.PendingPiApproval, row.State);
        Assert.Equal(Guid.Empty, row.WorkflowRequestId);
        Assert.Equal(row.CreatedAt, row.UpdatedAt);
        sut.Workflow.Verify(w => w.StartAsync<AssignResourceFlow>(
            It.Is<object>(i => ((AssignResourceFlowInput)i).Slug == row.Slug),
            It.Is<SubmitOptions?>(o => o!.IdempotencyKey == row.Slug),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_ReturnRequired_When_TheRouteShortNameIsInvalid()
    {
        var sut = new Sut { RouteShortName = "" };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status400BadRequest, "validation.required", "Project short name is required.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnProjectNotFoundBeforeValidatingTheBody_When_TheCallerIsOutOfScope()
    {
        var sut = new Sut { RouteShortName = "other", Body = new("", "", "") };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found", "Project not found.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnInvalidProviderNameBeforeTheResourceName_When_BothAreInvalid()
    {
        var sut = new Sut { Body = new(" ", "", PiEmail) };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status400BadRequest, ProviderErrorCodes.InvalidName, "ProviderName cannot be empty");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnInvalidResourceName_When_TheResourceNameIsEmpty()
    {
        var sut = new Sut { Body = new("acme", "", PiEmail) };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status400BadRequest, "resource.invalid_name", "Resource name is invalid.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnInvalidFormat_When_ThePrimaryInvestigatorEmailIsBlank()
    {
        var sut = new Sut { Body = new("acme", "gpu", "  ") };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status400BadRequest, "validation.invalid_format", "A valid primary investigator email (max 100 characters) is required.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_TheProjectIsMissing()
    {
        var sut = new Sut { Project = null };

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status404NotFound, "project.not_found", "Project not found.");
        sut.Providers.VerifyNoOtherCalls();
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnNoSupportEmail_When_TheProviderHasNoSupportEmail()
    {
        var sut = new Sut();
        sut.Providers.Setup(q => q.GetProviderDetailsAsync(It.IsAny<ProviderName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProviderDetail(
                ProviderName.From("acme"), null, null,
                null, EOperationMode.Unspecified, ServiceName.Default, IsPrivate: false, CountryCode: "CZ", IsDeleted: false, []));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status422UnprocessableEntity, ResourceRequestErrorCodes.ProviderNoSupportEmail, "Provider 'acme' has no support email to notify.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnDuplicateActiveWithoutInserting_When_AnActiveRequestExists()
    {
        var sut = new Sut();
        sut.Query.Setup(q => q.HasActiveRequestAsync("proj", "acme", "gpu", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status409Conflict, "resource_request.duplicate_active", "An active request for resource 'gpu' already exists on project 'proj'.");
        sut.VerifyNothingWritten();
    }

    [Fact]
    public async Task Should_ReturnFlowStartFailedAndDeleteTheRow_When_TheEngineRejectsTheStart()
    {
        var sut = new Sut();
        sut.Workflow.Setup(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("engine down"));

        var result = await sut.InvokeAsync();

        AssertProblem(result, StatusCodes.Status502BadGateway, "resource_request.flow_start_failed", "Failed to start the assignment workflow. Please retry.");
        sut.Store.Verify(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_PropagateWithoutDeletingTheRow_When_TheStartFailsBecauseTheRequestWasCancelled()
    {
        var sut = new Sut();
        using var cts = new CancellationTokenSource();
        sut.Workflow.Setup(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await cts.CancelAsync();
                throw new OperationCanceledException(cts.Token);
            });

        await Assert.ThrowsAsync<OperationCanceledException>(() => sut.InvokeAsync(cts.Token));

        sut.Store.Verify(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
