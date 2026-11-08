using System.Collections.Concurrent;

using Core.DataAccess.Queries;
using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Projects;

/// <summary>
/// Decision Q4 on the modular read side: <see cref="ProjectDetailsQuery.GetProjectHeappeLinksAsync"/> skips every
/// link without a valid HEAPPE_IDENTIFIER and logs exactly one warning per skipped link, naming its location.
/// </summary>
public sealed class ProjectDetailsQueryHeappeLinkWarningTests : SqliteQueryHarness
{
    private readonly CapturingLogger<ProjectDetailsQuery> _logger = new();

    private SqliteCoreSeed Seed => new(ServiceProvider);

    protected override void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ILogger<ProjectDetailsQuery>>(_logger);
        services.AddScoped<IProjectQuery, ProjectDetailsQuery>();
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_logs_one_warning_per_link_without_an_identifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var project = await Seed.ProjectAsync("proj-a");
        await LinkedAssignmentAsync(acme, project, "res-url-only", "hpc-url-only", [("HEAPPE_URL", "https://hpc.example.org")]);
        await LinkedAssignmentAsync(acme, project, "res-bad-id", "hpc-bad-id", [("HEAPPE_IDENTIFIER", "Not Valid!")]);
        await LinkedAssignmentAsync(acme, project, "res-ok", "hpc-ok", [("HEAPPE_IDENTIFIER", "heappe-ok")]);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        Assert.Equal("res-ok", Assert.Single(links).ResourceName);
        var warnings = _logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.Equal(2, warnings.Count);
        Assert.Single(warnings, m => m.Contains("'hpc-url-only'", StringComparison.Ordinal) && m.Contains("proj-a", StringComparison.Ordinal));
        Assert.Single(warnings, m => m.Contains("'hpc-bad-id'", StringComparison.Ordinal) && m.Contains("res-bad-id", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetProjectHeappeLinksAsync_logs_no_warning_when_every_link_has_an_identifier()
    {
        var ct = TestContext.Current.CancellationToken;
        var acme = await Seed.ProviderAsync("acme");
        var project = await Seed.ProjectAsync("proj-a");
        await LinkedAssignmentAsync(acme, project, "res-ok", "hpc-ok", [("HEAPPE_IDENTIFIER", "heappe-ok")]);

        var links = await QueryAsync(q => q.GetProjectHeappeLinksAsync(new ProjectShortName("proj-a"), ct));

        Assert.NotNull(links);
        Assert.Single(links);
        Assert.DoesNotContain(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    private async Task<T> QueryAsync<T>(Func<IProjectQuery, Task<T>> act)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        return await act(scope.ServiceProvider.GetRequiredService<IProjectQuery>());
    }

    private async Task LinkedAssignmentAsync(
        ProviderId provider, ProjectId project, string resourceName, string locationName, (string Name, string Value)[] values)
    {
        var location = await Seed.LocationAsync(provider, locationName, ELocationType.HPC);
        var resource = await Seed.ResourceAsync(provider, resourceName);
        await Seed.AssignAsync(resource, location, values: values);
        await Seed.LinkAsync(project, resource);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        internal readonly record struct Entry(LogLevel Level, string Message);

        private readonly ConcurrentQueue<Entry> _entries = new();

        internal IReadOnlyCollection<Entry> Entries => _entries;

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _entries.Enqueue(new Entry(logLevel, formatter(state, exception)));
    }
}
