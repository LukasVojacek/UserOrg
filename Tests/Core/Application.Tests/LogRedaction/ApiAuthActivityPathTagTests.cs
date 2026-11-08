using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.AspNetCore.Http;

using UserOrganizationManagement.API.Authentication;

namespace UserOrganizationManagement.Application.Tests.LogRedaction;

/// <summary>
/// The exported <c>UserOrg.API.Auth</c> activities carry no <c>auth.path</c> tag: the raw path holds the public
/// action-link token, and the parent HTTP span's redacted <c>url.path</c> already carries the path.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class ApiAuthActivityPathTagTests
{
    private const string PathTag = "auth.path";

    [Fact]
    public async Task Should_ObserveTheMessageReceivedAndAuthenticationFailedActivities_When_AFailingGatewayJwtArrives()
    {
        var activities = await RecordAuthActivitiesAsync();

        Assert.Multiple(
            () => Assert.Contains(activities, activity => activity.OperationName == AuthTelemetry.ActivityMessageReceived),
            () => Assert.Contains(activities, activity => activity.OperationName == AuthTelemetry.ActivityAuthenticationFailed));
    }

    [Fact]
    public async Task Should_CarryNoPathTagOnAnyAuthActivity_When_AFailingGatewayJwtArrivesOnAnActionLinkRoute()
    {
        var activities = await RecordAuthActivitiesAsync();

        Assert.NotEmpty(activities);
        Assert.Empty(activities
            .Where(activity => activity.GetTagItem(PathTag) is not null)
            .Select(activity => $"{activity.OperationName} {PathTag}={activity.GetTagItem(PathTag)}"));
    }

    private static async Task<IReadOnlyList<Activity>> RecordAuthActivitiesAsync()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AuthTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);

        await using var host = LogCaptureApiFactory.Start();
        await host.SendAsync(HttpMethods.Post, "/api/v2/Public/ActionLinks/abc123", LogCaptureApiFactory.MintForeignToken());

        return [.. stopped];
    }
}