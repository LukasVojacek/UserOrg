using Core.DataAccess.Contexts;
using Core.DataAccess.Queries;
using Core.DataAccess.Events;
using Core.DataAccess.Queries.Contracts;
using Core.Domain;
using Core.Domain.Request;

using Forge.Core.Features.Role.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RequestAggregate = Core.Domain.Request.Request;

namespace Core.UnitTests;

/// <summary>
/// Regression tests for <see cref="RequestDetailsQuery"/> using the EF Core in-memory provider.
/// Both queries previously projected through a non-existent <c>"User"</c> navigation
/// (<c>EF.Property&lt;User&gt;(r, "User")</c>), which throws a model-level translation error and
/// surfaced as a 500 on <c>v2/requests</c> and <c>v2/public/requests/{id}</c>. The in-memory
/// provider reproduces that failure because the error is raised while compiling the query, not
/// during SQL generation — so these tests cover the fix without a live database.
/// </summary>
public sealed class RequestQueryTests : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public RequestQueryTests()
    {
        var dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<DomainEventChannel>();
        services.AddScoped<IRoleService, UserDatabaseAccessTests.TestRoleService>();
        services.Configure<WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped(typeof(IDataQuery<>), typeof(DataQuery<>));
        services.AddScoped<IRequestQuery, RequestDetailsQuery>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Should_ProjectRequestContactEmail_When_ListingRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        var contactEmail = Email.Create("aaa@vsb.cz");

        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var request = RequestAggregate.Create(
                contactEmail,
                ProjectId.From(Guid.NewGuid()),
                UserId.From(Guid.NewGuid()));
            context.Requests.Add(request);
            await context.SaveChangesAsync(ct);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();

        var result = await query.GetRequestsAsync(new RequestFilter(null, null, null), 1, 50, ct);

        var summary = Assert.Single(result.Items);
        Assert.Equal(contactEmail, summary.ContactEmail);
        Assert.Equal(1, result.TotalItems);
    }

    [Fact]
    public async Task Should_PageStably_WithoutDupesOrGaps_When_ManyRequestsShareOneProject()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = ProjectId.From(Guid.NewGuid());
        const int total = 5;

        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            for (var i = 0; i < total; i++)
            {
                context.Requests.Add(RequestAggregate.Create(
                    Email.Create($"user{i}@vsb.cz"),
                    projectId,
                    UserId.From(Guid.NewGuid())));
            }
            await context.SaveChangesAsync(ct);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();

        // All rows share the same ProjectId, so a non-unique ORDER BY would let rows shuffle across pages.
        var page1 = await query.GetRequestsAsync(new RequestFilter(null, null, null), 1, 2, ct);
        var page2 = await query.GetRequestsAsync(new RequestFilter(null, null, null), 2, 2, ct);
        var page3 = await query.GetRequestsAsync(new RequestFilter(null, null, null), 3, 2, ct);

        Assert.Equal(total, page1.TotalItems);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);

        var seen = page1.Items.Concat(page2.Items).Concat(page3.Items)
            .Select(r => r.ContactEmail!.Value)
            .ToList();

        Assert.Equal(total, seen.Distinct().Count());
    }

    [Fact]
    public async Task Should_ReturnEveryRowExactlyOnce_When_PagingAcrossMultipleProjects()
    {
        var ct = TestContext.Current.CancellationToken;
        const int total = 7;
        const int limit = 3;

        var seeded = new List<string>();
        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            for (var i = 0; i < total; i++)
            {
                var email = $"multi{i}@vsb.cz";
                seeded.Add(email);
                context.Requests.Add(RequestAggregate.Create(
                    Email.Create(email),
                    ProjectId.From(Guid.NewGuid()),
                    UserId.From(Guid.NewGuid())));
            }
            await context.SaveChangesAsync(ct);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();

        var collected = new List<string>();
        var filter = new RequestFilter(null, null, null);
        for (var page = 1; ; page++)
        {
            var result = await query.GetRequestsAsync(filter, page, limit, ct);

            // Scoped total stays stable across every page.
            Assert.Equal(total, result.TotalItems);

            if (result.Items.Count == 0)
                break;

            collected.AddRange(result.Items.Select(r => r.ContactEmail!.Value.Value));
        }

        // No overlap and no gaps: every seeded row appears exactly once across all pages.
        Assert.Equal(total, collected.Count);
        Assert.Equal(total, collected.Distinct().Count());
        Assert.Equal(seeded.OrderBy(e => e), collected.OrderBy(e => e));
    }

    [Fact]
    public async Task Should_ReturnStableSlices_When_PagedRepeatedly()
    {
        var ct = TestContext.Current.CancellationToken;
        var projectId = ProjectId.From(Guid.NewGuid());
        const int total = 6;
        const int limit = 2;

        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();
            for (var i = 0; i < total; i++)
            {
                context.Requests.Add(RequestAggregate.Create(
                    Email.Create($"stable{i}@vsb.cz"),
                    projectId,
                    UserId.From(Guid.NewGuid())));
            }
            await context.SaveChangesAsync(ct);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();
        var filter = new RequestFilter(null, null, null);

        // The same page returns the same slice every time (the unique Id tie-breaker keeps order stable).
        var page2First = await query.GetRequestsAsync(filter, 2, limit, ct);
        var page2Second = await query.GetRequestsAsync(filter, 2, limit, ct);

        Assert.Equal(
            page2First.Items.Select(r => r.ContactEmail!.Value),
            page2Second.Items.Select(r => r.ContactEmail!.Value));

        // Pages 1..3 together cover all rows once, no dupes across page boundaries.
        var page1 = await query.GetRequestsAsync(filter, 1, limit, ct);
        var page3 = await query.GetRequestsAsync(filter, 3, limit, ct);

        var all = page1.Items.Concat(page2First.Items).Concat(page3.Items)
            .Select(r => r.ContactEmail!.Value)
            .ToList();

        Assert.Equal(total, all.Count);
        Assert.Equal(total, all.Distinct().Count());
    }

    [Fact]
    public async Task Should_CompileAndReturnNull_When_PublicRequestSummaryNotFound()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();

        // The buggy projection threw "property 'User' does not exist" while compiling this query;
        // the join-based fix must translate cleanly and simply return null for a missing request.
        var summary = await query.GetPublicRequestSummaryAsync(Guid.NewGuid(), ct);

        Assert.Null(summary);
    }

    [Theory]
    [InlineData("slug-1-req-abc")]
    [InlineData(null)]
    public async Task Should_ProjectTheRequestSlug_When_ReadingThePublicRequestSummary(string? slug)
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = UserId.From(Guid.NewGuid());
        var projectId = ProjectId.From(Guid.NewGuid());
        var requestId = Guid.CreateVersion7();

        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var context = seedScope.ServiceProvider.GetRequiredService<DatabaseContext>();

            var user = Domain.User.User.CreateUser(Email.Create("slug@vsb.cz"), UserName.Create("slug-user"), IdpId.Create("slug-idp"));
            context.Add(user);
            context.Entry(user).Property("Id").CurrentValue = userId;

            var project = Domain.Project.Project.Create(
                new ProjectShortName("SLUG-1"),
                ProjectName.Create("SLUG-1"),
                ProjectDomainType.FromName(DomainTypeName.Create("domain-slug")),
                UserId.New(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30),
                ServiceName.Default);
            context.Add(project);
            context.Entry(project).Property("Id").CurrentValue = projectId;
            foreach (var domainTypeEntry in context.ChangeTracker.Entries<ProjectDomainType>().ToList())
            {
                var key = domainTypeEntry.Metadata.FindPrimaryKey()!.Properties[0].Name;
                domainTypeEntry.Property(key).CurrentValue = new ProjectDomainTypeId(9100);
            }

            var request = RequestAggregate.Create(
                Email.Create("slug@vsb.cz"), projectId, userId, RequestType.ProjectResourceAssignment, slug: slug);
            context.Add(request).Property("Id").CurrentValue = requestId;

            await context.SaveChangesAsync(ct);
        }

        await using var scope = _serviceProvider.CreateAsyncScope();
        var query = scope.ServiceProvider.GetRequiredService<IRequestQuery>();

        var summary = await query.GetPublicRequestSummaryAsync(requestId, ct);

        Assert.NotNull(summary);
        Assert.Equal(slug, summary.Slug);
    }

    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
