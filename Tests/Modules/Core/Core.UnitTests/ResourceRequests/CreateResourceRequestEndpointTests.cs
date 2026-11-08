using Core.DataAccess.Queries.Contracts;
using Core.DataAccess.ResourceRequests;
using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.ResourceRequest.CreateResourceRequest;
using Core.Features.ResourceRequest.Flow;
using Core.Shared.Auth;
using Core.UnitTests.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using WorkflowEngine.Contracts.Models;
using WorkflowEngine.Contracts.Ports;

using ProjectAggregate = Core.Domain.Project.Project;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Endpoint unit tests for <see cref="CreateResourceRequestEndpoint"/> (B01, routes 1): scope anti-leak,
/// the legacy-shape contract (free-text resource name + requester-supplied PI email, DEC-1), the
/// no-support-email 422 (DEC-5), the two conflict guards, slug suffixing across providers, and the
/// row-insert-first / cleanup-on-flow-start-failure contract.
/// </summary>
public sealed class CreateResourceRequestEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProjectShortName Short = ProjectShortName.Create("proj");
    private const string PiEmail = "pi@example.org";
    private const string SupportEmail = "support@acme.example";

    private static Caller InScope() => new(
        new IdpId("idp-1"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Manager"] = new HashSet<ProjectShortName> { Short } });

    private static Caller OutOfScope() => new(
        new IdpId("idp-1"), "Manager", null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>());

    private static ProjectResource Resource(string provider = "acme", string name = "gpu") =>
        ProjectResource.Create(
            ResourceName.Create(name),
            Provider.Create(ProviderName.From(provider), 1, supportEmail: Email.Create(SupportEmail)), UserId.From(Guid.NewGuid()),
            Now, Now.AddYears(1), primaryInvestigatorEmail: Email.Create(PiEmail));

    private static ProjectAggregate Project() => ProjectAggregate.Create(
        Short, Core.Domain.Project.ProjectName.Create("Project"),
        Core.Domain.Project.ProjectDomainType.FromName(Core.Domain.Project.DomainTypeName.Create("HPC")),
        UserId.New(), Now, Now.AddYears(1), ServiceName.Default);

    private static ProviderDetail Detail(string name, bool withSupportEmail) => new(
        ProviderName.From(name), null, withSupportEmail ? Email.Create(SupportEmail) : null,
        null, EOperationMode.Unspecified, ServiceName.Default, IsPrivate: false, CountryCode: "CZ", IsDeleted: false, []);

    private sealed record Harness(
        IResult Result,
        Mock<IResourceRequestProjectionStore> Store,
        Mock<IWorkflowClient> Workflow,
        string? IdempotencyKey,
        IReadOnlyList<ProjectShortName>? ProviderLookupScope);

    private static async Task<Harness> InvokeAsync(
        Caller? caller = null,
        bool providerHasSupportEmail = true,
        bool providerResolves = true,
        ProjectAggregate? project = null,
        bool hasActiveRequest = false,
        IReadOnlyList<string>? existingSlugs = null,
        bool flowThrows = false,
        IReadOnlyList<Exception?>? insertAttempts = null,
        string providerName = "acme",
        string resourceName = "gpu",
        string primaryInvestigatorEmail = PiEmail,
        ILogger<CreateResourceRequestEndpoint.AuditCategory>? logger = null)
    {
        project ??= Project();

        var projects = new Mock<IRepository<ProjectAggregate, ProjectShortName>>();
        projects.Setup(r => r.FindAsync(It.IsAny<ProjectShortName>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<ProjectAggregate?>(project));

        // The read port collapses "no such provider" and "private, out of the caller's reach" into the same
        // null (ProviderDetailsQuery, ADR-0018) — `providerResolves: false` stands for both.
        IReadOnlyList<ProjectShortName>? providerLookupScope = null;
        var providers = new Mock<IProviderQuery>();
        providers.Setup(q => q.GetProviderDetailsAsync(It.IsAny<ProviderName>(), It.IsAny<IReadOnlyList<ProjectShortName>?>(), It.IsAny<CancellationToken>()))
            .Callback((ProviderName _, IReadOnlyList<ProjectShortName>? scope, CancellationToken _) => providerLookupScope = scope)
            .ReturnsAsync(providerResolves ? Detail(providerName, providerHasSupportEmail) : null);

        var query = new Mock<IProjectResourceRequestQuery>();
        query.Setup(q => q.HasActiveRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasActiveRequest);
        query.Setup(q => q.ListSlugsByBaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSlugs ?? []);

        var store = new Mock<IResourceRequestProjectionStore>();
        if (insertAttempts is { Count: > 0 })
        {
            var seq = store.SetupSequence(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()));
            foreach (var ex in insertAttempts)
                seq = ex is null ? seq.Returns(Task.CompletedTask) : seq.ThrowsAsync(ex);
        }
        else
        {
            store.Setup(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        store.Setup(s => s.DeleteBySlugAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var workflow = new Mock<IWorkflowClient>();
        string? idempotencyKey = null;
        var setup = workflow.Setup(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()))
            .Callback((object _, SubmitOptions? o, CancellationToken _) => idempotencyKey = o?.IdempotencyKey);
        if (flowThrows)
            setup.ThrowsAsync(new InvalidOperationException("engine down"));
        else
            setup.ReturnsAsync(Guid.CreateVersion7());

        var result = await CreateResourceRequestEndpoint.HandleAsync(
            Short.Value,
            new CreateResourceRequestEndpoint.Request(providerName, resourceName, primaryInvestigatorEmail),
            caller ?? InScope(),
            projects.Object, providers.Object, query.Object, store.Object, workflow.Object,
            new RecordingAuditLogger<CreateResourceRequestEndpoint.AuditCategory>(), logger ?? NullLogger<CreateResourceRequestEndpoint.AuditCategory>.Instance,
            CancellationToken.None);

        return new Harness(result, store, workflow, idempotencyKey, providerLookupScope);
    }

    private static int? StatusOf(IResult result) => Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode;

    private static string? CodeOf(IResult result) =>
        Assert.IsType<ProblemHttpResult>(result).ProblemDetails.Extensions["code"] as string;

    [Fact]
    public async Task Should_Return202WithSlug_And_StartFlow_When_Valid()
    {
        var h = await InvokeAsync();

        var accepted = Assert.IsType<Accepted<CreateResourceRequestEndpoint.Response>>(h.Result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.Equal("proj-gpu", accepted.Value!.Slug);
        Assert.Equal("v2/Projects/proj/ResourceRequests", accepted.Location);
        h.Store.Verify(
            s => s.InsertAsync(It.Is<ProjectResourceRequest>(r => r.Slug == "proj-gpu" && r.ContactEmail == PiEmail), It.IsAny<CancellationToken>()),
            Times.Once);
        h.Workflow.Verify(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("proj-gpu", h.IdempotencyKey);
    }

    [Fact]
    public async Task Should_Succeed_When_ResourceName_Does_Not_Exist()
    {
        // No ProjectResource is ever looked up by this endpoint (DEC-1) — a free-text name that matches
        // nothing under the provider is still accepted.
        var h = await InvokeAsync(resourceName: "brand-new-cluster");

        Assert.Equal(StatusCodes.Status202Accepted, StatusOf(h.Result));
    }

    [Fact]
    public async Task Should_Return422_When_Provider_Has_No_SupportEmail()
    {
        var h = await InvokeAsync(providerHasSupportEmail: false);

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, StatusOf(h.Result));
        Assert.Equal("resource_request.provider_no_support_email", CodeOf(h.Result));
        h.Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnProviderNotFound_When_ProviderDoesNotResolveForCaller()
    {
        var h = await InvokeAsync(providerResolves: false);

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(h.Result));
        Assert.Equal("provider.not_found", CodeOf(h.Result));
        h.Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ResolveProviderWithinCallerProjectScope_So_PrivateProviderExistenceCannotLeak()
    {
        // The scope argument is what makes a private provider the caller cannot reach return the same 404 as a
        // missing one (ADR-0018). Passing null here would resolve any provider and turn the 404 into an
        // existence oracle for every project Owner/Manager.
        var h = await InvokeAsync();

        Assert.Equal([Short], h.ProviderLookupScope);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task Should_ReturnValidationError_When_PrimaryInvestigatorEmail_Invalid(string email)
    {
        var h = await InvokeAsync(primaryInvestigatorEmail: email);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(h.Result));
    }

    [Fact]
    public async Task Should_ReturnValidationError_When_PrimaryInvestigatorEmail_ExceedsMaxLength()
    {
        var localPart = new string('a', 95);
        var h = await InvokeAsync(primaryInvestigatorEmail: $"{localPart}@example.org");

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(h.Result));
    }

    [Fact]
    public async Task Should_ReturnProjectNotFound_When_CallerOutOfScope()
    {
        var h = await InvokeAsync(caller: OutOfScope());

        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(h.Result));
        Assert.Equal("project.not_found", CodeOf(h.Result));
        h.Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ResourceAlreadyAttached()
    {
        var project = Project();
        var resource = Resource();
        Assert.True(project.AssignResource(resource).IsSuccess);

        var h = await InvokeAsync(project: project);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(h.Result));
        Assert.Equal("resource_request.already_attached", CodeOf(h.Result));
    }

    [Fact]
    public async Task Should_ReturnConflict_When_ActiveRequestExists()
    {
        var h = await InvokeAsync(hasActiveRequest: true);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(h.Result));
        Assert.Equal("resource_request.duplicate_active", CodeOf(h.Result));
    }

    [Fact]
    public async Task Should_SuffixSlug_When_BaseSlugAlreadyUsedByAnotherProvider()
    {
        var h = await InvokeAsync(existingSlugs: ["proj-gpu"], providerName: "globex");

        var accepted = Assert.IsType<Accepted<CreateResourceRequestEndpoint.Response>>(h.Result);
        Assert.Equal("proj-gpu-2", accepted.Value!.Slug);
    }

    [Fact]
    public async Task Should_ReturnDuplicateActiveConflict_When_InsertLosesSingleActiveRace()
    {
        var conflict = new ResourceRequestConflictException(ResourceRequestConflict.SingleActive, new InvalidOperationException());
        var h = await InvokeAsync(insertAttempts: [conflict]);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(h.Result));
        Assert.Equal("resource_request.duplicate_active", CodeOf(h.Result));
        h.Workflow.Verify(w => w.StartAsync<AssignResourceFlow>(It.IsAny<object>(), It.IsAny<SubmitOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_RecomputeSlugAndSucceed_When_FirstInsertLosesSlugRace()
    {
        var slugConflict = new ResourceRequestConflictException(ResourceRequestConflict.Slug, new InvalidOperationException());
        var h = await InvokeAsync(insertAttempts: [slugConflict, null]);

        Assert.Equal(StatusCodes.Status202Accepted, StatusOf(h.Result));
        h.Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Should_ReturnSlugConflict_When_SlugRaceSurvivesRetry()
    {
        var slugConflict = new ResourceRequestConflictException(ResourceRequestConflict.Slug, new InvalidOperationException());
        var h = await InvokeAsync(insertAttempts: [slugConflict, slugConflict]);

        Assert.Equal(StatusCodes.Status409Conflict, StatusOf(h.Result));
        Assert.Equal("resource_request.slug_conflict", CodeOf(h.Result));
    }

    [Fact]
    public async Task Should_CleanUpRow_When_FlowStartFails()
    {
        var h = await InvokeAsync(flowThrows: true);

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(h.Result));
        h.Store.Verify(s => s.InsertAsync(It.IsAny<ProjectResourceRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        h.Store.Verify(s => s.DeleteBySlugAsync("proj-gpu", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_LogTheEngineFailureWithItsException_When_FlowStartFails()
    {
        var logger = new Mock<ILogger<CreateResourceRequestEndpoint.AuditCategory>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        var h = await InvokeAsync(flowThrows: true, logger: logger.Object);

        Assert.Equal(StatusCodes.Status502BadGateway, StatusOf(h.Result));
        logger.Verify(l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.Is<InvalidOperationException>(e => e.Message == "engine down"),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }
}
