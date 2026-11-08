using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;

namespace Core.UnitTests.Requests;

/// <summary>
/// Characterization tests for <see cref="RequestDetailsQuery"/> on SQLite: every optional filter of
/// <see cref="RequestDetailsQuery.GetRequestsAsync"/> and its projection, the public request summary with and
/// without its optional parts, and the pending self-onboarding lookup. They pin today's translated SQL so the
/// optional-filter chain can be restructured without a behaviour change.
/// </summary>
public sealed class RequestDetailsQuerySqliteTests : SqliteQueryHarness
{
    private static readonly DateTimeOffset EarlyValidTo = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LateValidTo = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DeletedValidTo = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services) =>
        services.AddScoped<IRequestQuery, RequestDetailsQuery>();

    [Fact]
    public async Task GetRequestsAsync_without_filters_lists_every_request_with_its_natural_keys()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var result = await QueryAsync(q => q.GetRequestsAsync(new RequestFilter(null, null, null), 1, 50, ct));

        Assert.Equal(6, result.TotalItems);
        Assert.Equal(
            [
                ("a1@example.org", "proj-a", "idp-1", (string?)"acme", (string?)"gpu-res", RequestState.Pending),
                ("a2@example.org", "proj-a", "idp-2", null, null, RequestState.Accepted),
                ("b1@example.org", "proj-b", "idp-1", "retired", null, RequestState.Pending),
                ("d1@example.org", "proj-gone", "idp-2", null, null, RequestState.Pending),
                ("o1@example.org", "proj-b", "idp-2", null, null, RequestState.Pending),
                ("x1@example.org", "proj-a", "idp-1", null, null, RequestState.Pending),
            ],
            result.Items
                .Select(r => (r.ContactEmail!.Value.Value, r.ProjectShortName.Value, r.IdpUserId.Value, r.ProviderName?.Value, r.ProjectResourceName?.Value, r.State))
                .OrderBy(r => r.Item1));
    }

    [Fact]
    public async Task GetRequestsAsync_lists_a_soft_deleted_request_because_its_projection_ignores_query_filters()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        var user = await Seed.UserAsync("idp-1");
        var deleted = await Seed.RequestAsync("deleted@example.org", project, user, deleted: true);

        var listed = await QueryAsync(q => q.GetRequestsAsync(new RequestFilter(null, null, null), 1, 50, ct));
        var summary = await QueryAsync(q => q.GetPublicRequestSummaryAsync(deleted, ct));

        Assert.Equal(["deleted@example.org"], Emails(listed));
        Assert.Null(summary);
    }

    [Fact]
    public async Task GetRequestsAsync_with_a_scope_keeps_only_requests_on_the_scoped_projects()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var scoped = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(null, null, null, ScopeShortNames: [new ProjectShortName("proj-a")]), 1, 50, ct));
        var empty = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(null, null, null, ScopeShortNames: []), 1, 50, ct));

        Assert.Equal(["a1@example.org", "a2@example.org", "x1@example.org"], Emails(scoped));
        Assert.Equal(3, scoped.TotalItems);
        Assert.Empty(empty.Items);
        Assert.Equal(0, empty.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_with_a_project_short_name_keeps_only_that_project()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var result = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(new ProjectShortName("proj-b"), null, null), 1, 50, ct));

        Assert.Equal(["b1@example.org", "o1@example.org"], Emails(result));
        Assert.Equal(2, result.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_with_a_user_idp_id_keeps_only_that_users_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var result = await QueryAsync(q => q.GetRequestsAsync(new RequestFilter(null, new IdpId("idp-2"), null), 1, 50, ct));

        Assert.Equal(["a2@example.org", "d1@example.org", "o1@example.org"], Emails(result));
        Assert.Equal(3, result.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_with_a_sender_idp_id_keeps_only_that_senders_requests()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var result = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(null, null, null, SenderIdpId: new IdpId("idp-1")), 1, 50, ct));

        Assert.Equal(["a1@example.org", "b1@example.org", "x1@example.org"], Emails(result));
        Assert.Equal(3, result.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_with_a_state_keeps_only_requests_in_that_state()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var result = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(null, null, RequestState.Accepted.Value), 1, 50, ct));

        Assert.Equal(["a2@example.org"], Emails(result));
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_combines_every_filter_as_a_conjunction()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();

        var hit = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(
                new ProjectShortName("proj-b"),
                new IdpId("idp-1"),
                RequestState.Pending.Value,
                ScopeShortNames: [new ProjectShortName("proj-a"), new ProjectShortName("proj-b")],
                SenderIdpId: new IdpId("idp-1")),
            1, 50, ct));
        var miss = await QueryAsync(q => q.GetRequestsAsync(
            new RequestFilter(
                new ProjectShortName("proj-b"),
                new IdpId("idp-1"),
                RequestState.Pending.Value,
                ScopeShortNames: [new ProjectShortName("proj-a")],
                SenderIdpId: new IdpId("idp-1")),
            1, 50, ct));

        Assert.Equal(["b1@example.org"], Emails(hit));
        Assert.Empty(miss.Items);
        Assert.Equal(0, miss.TotalItems);
    }

    [Fact]
    public async Task GetRequestsAsync_pages_the_filtered_set_and_counts_it_whole()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedListGraphAsync();
        var filter = new RequestFilter(null, new IdpId("idp-2"), null);

        var page1 = await QueryAsync(q => q.GetRequestsAsync(filter, 1, 2, ct));
        var page2 = await QueryAsync(q => q.GetRequestsAsync(filter, 2, 2, ct));

        Assert.Equal(3, page1.TotalItems);
        Assert.Equal(3, page2.TotalItems);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2.Items);
        Assert.Equal(
            ["a2@example.org", "d1@example.org", "o1@example.org"],
            Emails(page1).Concat(Emails(page2)).Order());
    }

    [Fact]
    public async Task GetPublicRequestSummaryAsync_reads_the_requestor_project_resource_provider_and_live_links()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = await Seed.ProviderAsync("retired", deactivated: true);
        var resource = await Seed.ResourceAsync(provider, "old-res", deleted: true);
        var project = await Seed.ProjectAsync("proj-a");
        var user = await Seed.UserAsync("idp-1");
        var requestId = await Seed.RequestAsync(
            "contact@example.org", project, user,
            type: RequestType.SelfOnboarding,
            state: RequestState.Rejected,
            resourceId: resource,
            providerId: provider,
            links:
            [
                (1, "accept-token", EarlyValidTo, false),
                (2, "reject-token", LateValidTo, false),
                (1, "stale-token", DeletedValidTo, true),
            ]);

        var summary = await QueryAsync(q => q.GetPublicRequestSummaryAsync(requestId, ct));

        Assert.NotNull(summary);
        Assert.Equal(
            new PublicRequestSummary(
                requestId,
                "user-idp-1",
                "idp-1@example.org",
                new ProjectShortName("proj-a"),
                "old-res",
                "retired",
                RequestState.Rejected.Value,
                LateValidTo,
                RequestType.SelfOnboarding.Value,
                "accept-token",
                "reject-token"),
            summary);
    }

    [Fact]
    public async Task GetPublicRequestSummaryAsync_leaves_the_optional_parts_empty_when_the_request_has_none()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        var user = await Seed.UserAsync("idp-1");
        var requestId = await Seed.RequestAsync("contact@example.org", project, user);

        var summary = await QueryAsync(q => q.GetPublicRequestSummaryAsync(requestId, ct));

        Assert.NotNull(summary);
        Assert.Null(summary.ProjectResourceName);
        Assert.Null(summary.ProviderName);
        Assert.Null(summary.AcceptActionLink);
        Assert.Null(summary.RejectActionLink);
        Assert.Equal(DateTimeOffset.MinValue, summary.ValidTo);
        Assert.Equal(RequestState.Pending.Value, summary.RequestState);
        Assert.Equal(RequestType.Default.Value, summary.RequestType);
    }

    [Fact]
    public async Task GetPublicRequestSummaryAsync_returns_null_for_an_unknown_request()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        var user = await Seed.UserAsync("idp-1");
        var known = await Seed.RequestAsync("contact@example.org", project, user);

        var summary = await QueryAsync(q => q.GetPublicRequestSummaryAsync(Guid.NewGuid(), ct));
        var control = await QueryAsync(q => q.GetPublicRequestSummaryAsync(known, ct));

        Assert.Null(summary);
        Assert.NotNull(control);
    }

    [Fact]
    public async Task FindPendingSelfOnboardingIdAsync_finds_only_the_pending_self_onboarding_request_of_the_user_on_the_project()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        var otherProject = await Seed.ProjectAsync("proj-b");
        var user = await Seed.UserAsync("idp-1");
        var otherUser = await Seed.UserAsync("idp-2");
        await Seed.RequestAsync("default@example.org", project, user);
        await Seed.RequestAsync("accepted@example.org", project, user, type: RequestType.SelfOnboarding, state: RequestState.Accepted);
        await Seed.RequestAsync("other-user@example.org", project, otherUser, type: RequestType.SelfOnboarding);
        await Seed.RequestAsync("other-project@example.org", otherProject, user, type: RequestType.SelfOnboarding);
        var expected = await Seed.RequestAsync("match@example.org", project, user, type: RequestType.SelfOnboarding);

        var found = await QueryAsync(q => q.FindPendingSelfOnboardingIdAsync(project, user, ct));

        Assert.Equal(expected, found);
    }

    [Fact]
    public async Task FindPendingSelfOnboardingIdAsync_returns_null_when_no_request_matches_every_condition()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = await Seed.ProjectAsync("proj-a");
        var otherProject = await Seed.ProjectAsync("proj-b");
        var user = await Seed.UserAsync("idp-1");
        var otherUser = await Seed.UserAsync("idp-2");
        await Seed.RequestAsync("default@example.org", project, user);
        await Seed.RequestAsync("accepted@example.org", project, user, type: RequestType.SelfOnboarding, state: RequestState.Accepted);
        await Seed.RequestAsync("other-user@example.org", project, otherUser, type: RequestType.SelfOnboarding);
        await Seed.RequestAsync("other-project@example.org", otherProject, user, type: RequestType.SelfOnboarding);
        await Seed.RequestAsync("deleted@example.org", project, user, type: RequestType.SelfOnboarding, deleted: true);

        var found = await QueryAsync(q => q.FindPendingSelfOnboardingIdAsync(project, user, ct));
        var control = await QueryAsync(q => q.FindPendingSelfOnboardingIdAsync(otherProject, user, ct));

        Assert.Null(found);
        Assert.NotNull(control);
    }

    private async Task SeedListGraphAsync()
    {
        var acme = await Seed.ProviderAsync("acme");
        var retired = await Seed.ProviderAsync("retired", deactivated: true);
        var gpu = await Seed.ResourceAsync(acme, "gpu-res");
        var projectA = await Seed.ProjectAsync("proj-a");
        var projectB = await Seed.ProjectAsync("proj-b");
        var goneProject = await Seed.ProjectAsync("proj-gone", deleted: true);
        var user1 = await Seed.UserAsync("idp-1");
        var user2 = await Seed.UserAsync("idp-2");

        await Seed.RequestAsync("a1@example.org", projectA, user1, resourceId: gpu, resourceName: ResourceName.Create("gpu-res"), providerId: acme);
        await Seed.RequestAsync("a2@example.org", projectA, user2, state: RequestState.Accepted);
        await Seed.RequestAsync("b1@example.org", projectB, user1, providerId: retired);
        await Seed.RequestAsync("o1@example.org", projectB, user2, providerId: ProviderId.New());
        await Seed.RequestAsync("d1@example.org", goneProject, user2);
        await Seed.RequestAsync("x1@example.org", projectA, user1, deleted: true);
    }

    private static IEnumerable<string> Emails(QueryPage<RequestSummary> page) =>
        page.Items.Select(r => r.ContactEmail!.Value.Value).Order();

    private async Task<T> QueryAsync<T>(Func<IRequestQuery, Task<T>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IRequestQuery>());
    }
}
