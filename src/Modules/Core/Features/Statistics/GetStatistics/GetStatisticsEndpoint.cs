using Core.Shared.Http;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Core.Features.Statistics.GetStatistics;

internal static class GetStatisticsEndpoint
{
    internal sealed record Response(
        int NumberOfProjects,
        int NumberOfUsers,
        int NumberOfResources,
        IReadOnlyList<WeeklyData> WeeklyData);

    internal sealed record WeeklyData(
        DateOnly From,
        DateOnly To,
        int ProjectsCreated,
        int UsersCreated,
        int ResourcesCreated);

    internal static async Task<IResult> HandleAsync(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromServices] IStatisticsQuery statistics,
        [FromServices] TimeProvider timeProvider,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Legacy defaults: fromDate = first of the current month, toDate = today; if the window is
        // empty or inverted, widen it to a single week from fromDate.
        var from = fromDate ?? new DateOnly(today.Year, today.Month, 1);
        var to = toDate ?? today;
        if (to <= from)
        {
            to = from.AddDays(7);
        }

        var timestamps = await statistics.GetCreationTimestampsAsync(ct);

        var weeks = WeeklyBuckets.GetWeeks(from, to, today);
        var rangeStart = from.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = to.ToDateTime(TimeOnly.MaxValue);

        var usersInRange = InRange(timestamps.Users, rangeStart, rangeEnd);
        var projectsInRange = InRange(timestamps.Projects, rangeStart, rangeEnd);
        var resourcesInRange = InRange(timestamps.Resources, rangeStart, rangeEnd);

        var weekly = weeks
            .Select(w => new WeeklyData(
                w.From,
                w.To,
                WeeklyBuckets.CountIn(projectsInRange, w),
                WeeklyBuckets.CountIn(usersInRange, w),
                WeeklyBuckets.CountIn(resourcesInRange, w)))
            .ToList();

        return Results.Ok(new Response(
            timestamps.Projects.Count,
            timestamps.Users.Count,
            timestamps.Resources.Count,
            weekly));
    }

    private static List<DateTime> InRange(IReadOnlyList<DateTime> timestamps, DateTime from, DateTime to)
        => timestamps.Where(t => t >= from && t <= to).ToList();

    extension(IEndpointRouteBuilder app)
    {
        internal IEndpointRouteBuilder MapGetStatisticsEndpoint()
        {
            app.MapGet("Statistics", HandleAsync)
                .CacheOutput(StatisticsCache.PolicyName)
                .WithETag()
                .MapDocumentation();
            return app;
        }
    }

    extension(RouteHandlerBuilder builder)
    {
        private RouteHandlerBuilder MapDocumentation() => builder
            .WithName("GetStatisticsV2")
            .WithSummary("Get weekly statistics")
            .WithDescription("Returns platform-wide totals and weekly creation counts for projects, users and resources.")
            .WithTags("Statistics")
            .Produces<Response>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}