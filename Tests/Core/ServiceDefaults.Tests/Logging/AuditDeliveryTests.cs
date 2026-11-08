using System.Net;

using Lukdrasil.StepUpLogging;
using Lukdrasil.StepUpLogging.Audit.EncryptedSpool;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Serilog.Events;

namespace UserOrg.ServiceDefaults.Tests.Logging;

/// <summary>
/// The audit spool's drain worker is the only retrying layer on the delivery edge and DeliveryTimeout its only
/// timeout (ADR 0096, QS-07): with userorg's ConfigureHttpClientDefaults registered after AddEncryptedSpoolAuditSink,
/// a drain attempt sends one POST and logs one Warning when it fails, an endpoint that does not answer is cut at the timeout, the record stays spooled,
/// and nothing is logged at Error, so the failure never steps logging up.
/// </summary>
[Collection(LoggingPipelineCollection.Name)]
public sealed class AuditDeliveryTests
{
    private const string DrainInterval = "00:01:00";

    [Fact(DisplayName = "Should_CutADeliveryAttemptAtTenSeconds_By_Default")]
    public async Task Should_CutADeliveryAttemptAtTenSeconds_By_Default()
    {
        await using var host = LoggingPipelineHost.Build();

        Assert.Equal(TimeSpan.FromSeconds(10), SpoolOptionsOf(host).DeliveryTimeout);
    }

    [Fact(DisplayName = "Should_LetConfigurationOverrideTheDeliveryTimeout")]
    public async Task Should_LetConfigurationOverrideTheDeliveryTimeout()
    {
        await using var host = LoggingPipelineHost.Build([new("EncryptedSpool:DeliveryTimeout", "00:00:20")]);

        Assert.Equal(TimeSpan.FromSeconds(20), SpoolOptionsOf(host).DeliveryTimeout);
    }

    [Fact(DisplayName = "Should_SendOnePostPerDrainAttempt_When_TheAuditEndpointAnswers500")]
    public async Task Should_SendOnePostPerDrainAttempt_When_TheAuditEndpointAnswers500()
    {
        var time = new FakeTimeProvider();
        using var endpoint = new StubEndpoint(HttpStatusCode.InternalServerError);
        await using var host = BuildWithStubs(time, endpoint, []);
        await SpoolOneRecordAsync(host);

        var worker = await StartDrainWorkerAsync(host);
        try
        {
            await AdvanceUntilAsync(time, () => endpoint.Calls > 0, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
            await LoggingPipelineHost.WaitUntilAsync(() => DrainWorkerWarnings(host) > 0);
            var attempts = DrainWorkerWarnings(host);

            await AdvanceUntilAsync(time, () => endpoint.Calls > attempts, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));

            Assert.True(attempts > 0, "the drain worker reported no failed attempt");
            Assert.Equal(attempts, endpoint.Calls);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "Should_KeepTheRecordSpooled_And_LogNoError_When_TheAuditEndpointDoesNotAnswer")]
    public async Task Should_KeepTheRecordSpooled_And_LogNoError_When_TheAuditEndpointDoesNotAnswer()
    {
        var time = new FakeTimeProvider();
        using var endpoint = new StubEndpoint(hang: true);
        await using var host = BuildWithStubs(time, endpoint, [new("EncryptedSpool:DeliveryTimeout", "00:00:00.300")]);
        await SpoolOneRecordAsync(host);

        var worker = await StartDrainWorkerAsync(host);
        try
        {
            await AdvanceUntilAsync(time, () => endpoint.Calls > 0, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
            await LoggingPipelineHost.WaitUntilAsync(() => DrainWorkerWarnings(host) > 0);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(DrainWorkerWarnings(host) > 0, "the drain worker logged nothing for the failed attempt, so the Error check below proves nothing");
        Assert.Equal(DrainWorkerWarnings(host), endpoint.Calls);
        Assert.DoesNotContain(host.Events, e => e.Level >= LogEventLevel.Error);
        Assert.Equal(LogEventLevel.Warning, host.Controller.LevelSwitch.MinimumLevel);
        Assert.NotEmpty(Directory.EnumerateFiles(SpoolOptionsOf(host).SpoolDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact(DisplayName = "Should_PostTheSpooledRecordAloneToTheAuditEndpoint_With_AContentLength")]
    public async Task Should_PostTheSpooledRecordAloneToTheAuditEndpoint_With_AContentLength()
    {
        var time = new FakeTimeProvider();
        using var endpoint = new StubEndpoint(HttpStatusCode.OK);
        await using var host = BuildWithStubs(time, endpoint, []);
        await SpoolOneRecordAsync(host);

        var worker = await StartDrainWorkerAsync(host);
        try
        {
            await AdvanceUntilAsync(time, () => endpoint.Calls > 0, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        var request = Assert.IsType<CapturedRequest>(endpoint.FirstRequest);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(new Uri("https://audit-proxy.logging-tests.example/audit"), request.Uri);
        Assert.Equal((byte)'{', request.Body.SkipWhile(b => char.IsWhiteSpace((char)b)).FirstOrDefault());
        Assert.Equal(request.Body.Length, request.ContentLength);
    }

    private static EncryptedSpoolOptions SpoolOptionsOf(LoggingPipelineHost host) =>
        host.App.Services.GetRequiredService<IOptions<EncryptedSpoolOptions>>().Value;

    private static LoggingPipelineHost BuildWithStubs(FakeTimeProvider time, StubEndpoint endpoint, IEnumerable<KeyValuePair<string, string?>> settings) =>
        LoggingPipelineHost.Build(
            [new("EncryptedSpool:DrainInterval", DrainInterval), .. settings],
            builder =>
            {
                builder.Services.AddSingleton<TimeProvider>(time);
                builder.Services.RemoveAll<IAuditPayloadEncryptor>();
                builder.Services.AddSingleton<IAuditPayloadEncryptor>(new PassThroughEncryptor());
                builder.Services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new StubPrimaryHandlerFilter(endpoint));
            });

    private static async Task SpoolOneRecordAsync(LoggingPipelineHost host) =>
        await host.App.Services.GetRequiredService<IAuditEventSink>().WriteAsync(AuditEvent.Success("audit.delivery-probe", "actor-1", "user"));

    private static async Task<IHostedService> StartDrainWorkerAsync(LoggingPipelineHost host)
    {
        var worker = Assert.Single(host.App.Services.GetServices<IHostedService>(), service => service.GetType().Name == "DrainWorker");
        await worker.StartAsync(CancellationToken.None);
        return worker;
    }

    private static int DrainWorkerWarnings(LoggingPipelineHost host) =>
        host.From(".DrainWorker").Count(e => e.Level == LogEventLevel.Warning);

    private static async Task AdvanceUntilAsync(FakeTimeProvider time, Func<bool> done, TimeSpan budget, TimeSpan step)
    {
        for (var advanced = TimeSpan.Zero; advanced < budget && !done(); advanced += step)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
            time.Advance(step);
        }

        await LoggingPipelineHost.WaitUntilAsync(done, TimeSpan.FromSeconds(1));
    }

    private sealed class StubPrimaryHandlerFilter(HttpMessageHandler endpoint) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = endpoint;
        };
    }

    private sealed class PassThroughEncryptor : IAuditPayloadEncryptor
    {
        public ValueTask<byte[]> EncryptAsync(ReadOnlyMemory<byte> payload) => ValueTask.FromResult(payload.ToArray());
    }

    private sealed class StubEndpoint(HttpStatusCode status = HttpStatusCode.OK, bool hang = false) : HttpMessageHandler
    {
        private int _calls;
        private CapturedRequest? _firstRequest;

        public int Calls => Volatile.Read(ref _calls);

        public CapturedRequest? FirstRequest => Volatile.Read(ref _firstRequest);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Interlocked.CompareExchange(ref _firstRequest, await CaptureAsync(request, cancellationToken), null);

            if (hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(status);
        }

        private static async Task<CapturedRequest> CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content;

            // Read the length before the body: buffering the body makes chunked content report a length.
            var contentLength = content?.Headers.ContentLength;
            var body = content is null ? [] : await content.ReadAsByteArrayAsync(cancellationToken);
            return new CapturedRequest(request.Method, request.RequestUri, contentLength, body);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, long? ContentLength, byte[] Body);
}