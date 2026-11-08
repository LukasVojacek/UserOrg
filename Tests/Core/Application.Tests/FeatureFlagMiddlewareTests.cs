using System.Security.Claims;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.FeatureManagement;

using UserOrganizationManagement.API.Middleware;

namespace UserOrganizationManagement.Application.Tests;

public sealed class FeatureFlagMiddlewareTests
{
    private const string BlockedAction = "feature_flag.request.blocked";

    private const string TokenRoute = "Public/ResourceRequestActions/{token}";

    private const string LiveToken = "live-token-3f9a";

    private sealed class RecordingAuditLogger : IAuditLogger<FeatureFlagMiddleware>
    {
        public List<AuditEvent> Events { get; } = [];

        public bool Throws { get; init; }

        public ValueTask AuditAsync(AuditEvent auditEvent)
        {
            Events.Add(auditEvent);
            if (Throws)
                throw new InvalidOperationException("sink down: " + LiveToken);

            return ValueTask.CompletedTask;
        }

        public ValueTask AuditAsync(AuditEvent auditEvent, Action<ILogger> log) => AuditAsync(auditEvent);
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }

    private sealed class RecordingLogger : ILogger<FeatureFlagMiddleware>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Records { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Records.Add((logLevel, formatter(state, exception), exception));
    }

    private static Mock<IFeatureManager> FeatureManager(string flag, bool enabled)
    {
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock.Setup(x => x.IsEnabledAsync(flag)).ReturnsAsync(enabled);
        return featureManagerMock;
    }

    private static DefaultHttpContext BlockedTokenRequest(object flagMetadata, params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length == 0 ? null : "test")),
        };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/v2/Public/ResourceRequestActions/" + LiveToken;
        httpContext.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(TokenRoute),
            0,
            new EndpointMetadataCollection(flagMetadata),
            "TokenEndpoint"));
        return httpContext;
    }

    private static async Task<(DefaultHttpContext Context, RecordingAuditLogger Audit, RecordingLogger Logger)> InvokeBlockedAsync(
        object flagMetadata, Mock<IFeatureManager> featureManager, bool auditThrows = false, params Claim[] claims)
    {
        var logger = new RecordingLogger();
        var audit = new RecordingAuditLogger { Throws = auditThrows };
        var middleware = new FeatureFlagMiddleware(_ => Task.CompletedTask, featureManager.Object, logger);
        var httpContext = BlockedTokenRequest(flagMetadata, claims);

        await middleware.InvokeAsync(httpContext, audit);

        return (httpContext, audit, logger);
    }

    [Fact(DisplayName = "Should_WriteDeniedAudit_When_DisabledWhen_Flag_Blocks_Request")]
    public async Task Should_WriteDeniedAudit_When_DisabledWhen_Flag_Blocks_Request()
    {
        var (context, audit, _) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: true),
            claims: [new Claim("sub", "f81d4fae@idp")]);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(BlockedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("ReadOnlyMode", auditEvent.Reason);
        Assert.Equal("f81d4fae", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.ActorType);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(TokenRoute, auditEvent.Data["route"]);
        Assert.Equal("POST", auditEvent.Data["method"]);
        Assert.Equal("f81d4fae@idp", auditEvent.Data["sub"]);
    }

    [Fact(DisplayName = "Should_WriteDeniedAudit_When_EnabledWhen_Flag_Blocks_Request")]
    public async Task Should_WriteDeniedAudit_When_EnabledWhen_Flag_Blocks_Request()
    {
        var (context, audit, _) = await InvokeBlockedAsync(
            new EnabledByFeatureFlagMetadata("V2Api"),
            FeatureManager("V2Api", enabled: false),
            claims: [new Claim(ClaimTypes.NameIdentifier, "f81d4fae@idp")]);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal(BlockedAction, auditEvent.Action);
        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("V2Api", auditEvent.Reason);
        Assert.Equal("f81d4fae", auditEvent.ActorId);
        Assert.NotNull(auditEvent.Data);
        Assert.Equal(TokenRoute, auditEvent.Data["route"]);
        Assert.Equal("f81d4fae@idp", auditEvent.Data["sub"]);
    }

    [Fact(DisplayName = "Should_RecordAnonymousActor_When_Blocked_Request_Has_No_Subject")]
    public async Task Should_RecordAnonymousActor_When_Blocked_Request_Has_No_Subject()
    {
        var (_, audit, _) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: true));

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("anonymous", auditEvent.ActorId);
        Assert.Equal("anonymous", auditEvent.ActorType);
        Assert.NotNull(auditEvent.Data);
        Assert.Null(auditEvent.Data["sub"]);
    }

    [Fact(DisplayName = "Should_RecordServiceActorType_When_Blocked_Caller_Used_An_ApiKey")]
    public async Task Should_RecordServiceActorType_When_Blocked_Caller_Used_An_ApiKey()
    {
        var (_, audit, _) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: true),
            claims: [new Claim("sub", "svc-waldur"), new Claim(EAuthorizationMethod.ClaimType, nameof(EAuthorizationMethod.ApiKey))]);

        var auditEvent = Assert.Single(audit.Events);
        Assert.Equal("svc-waldur", auditEvent.ActorId);
        Assert.Equal("service", auditEvent.ActorType);
    }

    [Fact(DisplayName = "Should_Never_Record_The_Raw_Path_When_Blocking")]
    public async Task Should_Never_Record_The_Raw_Path_When_Blocking()
    {
        var (_, audit, _) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: true));

        var auditEvent = Assert.Single(audit.Events);
        Assert.NotNull(auditEvent.Data);
        Assert.DoesNotContain(auditEvent.Data.Values, value => value is string text && text.Contains(LiveToken, StringComparison.Ordinal));
        Assert.DoesNotContain(LiveToken, auditEvent.Reason ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveToken, auditEvent.TargetId ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Should_Still_Return_404_And_Log_Warning_When_Audit_Write_Fails")]
    public async Task Should_Still_Return_404_And_Log_Warning_When_Audit_Write_Fails()
    {
        var (context, _, logger) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: true),
            auditThrows: true);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        var warning = Assert.Single(logger.Records, record => record.Level == LogLevel.Warning);
        Assert.Contains(BlockedAction, warning.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AuditOutcome.Denied), warning.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LiveToken, warning.Message, StringComparison.Ordinal);
        Assert.Null(warning.Exception);
    }

    [Fact(DisplayName = "Should_Not_Audit_When_Request_Passes_The_Flag_Gate")]
    public async Task Should_Not_Audit_When_Request_Passes_The_Flag_Gate()
    {
        var (context, audit, _) = await InvokeBlockedAsync(
            new DisabledByFeatureFlagMetadata("ReadOnlyMode"),
            FeatureManager("ReadOnlyMode", enabled: false));

        Assert.NotEqual(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(audit.Events);
    }

    [Fact(DisplayName = "Should_Return_404_When_Feature_Flag_Is_Enabled")]
    public async Task Should_Return_404_When_Feature_Flag_Is_Enabled()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("ReadOnlyMode"))
            .ReturnsAsync(true);

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var middleware = new FeatureFlagMiddleware(
            _ => Task.CompletedTask,
            featureManagerMock.Object,
            logger
        );

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/requests";

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new DisabledByFeatureFlagMetadata("ReadOnlyMode")
            ),
            "TestEndpoint"
        );

        httpContext.SetEndpoint(endpoint);

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact(DisplayName = "Should_Continue_When_Feature_Flag_Is_Disabled")]
    public async Task Should_Continue_When_Feature_Flag_Is_Disabled()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("ReadOnlyMode"))
            .ReturnsAsync(false);

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var nextMiddlewareCalled = false;
        RequestDelegate next = _ =>
        {
            nextMiddlewareCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new FeatureFlagMiddleware(next, featureManagerMock.Object, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/requests";

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new DisabledByFeatureFlagMetadata("ReadOnlyMode")
            ),
            "TestEndpoint"
        );

        httpContext.SetEndpoint(endpoint);

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.True(nextMiddlewareCalled);
        Assert.NotEqual(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }

    [Fact(DisplayName = "Should_Continue_When_No_Metadata_Found")]
    public async Task Should_Continue_When_No_Metadata_Found()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var nextMiddlewareCalled = false;
        RequestDelegate next = _ =>
        {
            nextMiddlewareCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new FeatureFlagMiddleware(next, featureManagerMock.Object, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/projects";

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(),
            "TestEndpoint"
        );

        httpContext.SetEndpoint(endpoint);

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.True(nextMiddlewareCalled);
        featureManagerMock.Verify(x => x.IsEnabledAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact(DisplayName = "Should_Continue_When_No_Endpoint_Set")]
    public async Task Should_Continue_When_No_Endpoint_Set()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var nextMiddlewareCalled = false;
        RequestDelegate next = _ =>
        {
            nextMiddlewareCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new FeatureFlagMiddleware(next, featureManagerMock.Object, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/requests";

        // No endpoint set

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.True(nextMiddlewareCalled);
        featureManagerMock.Verify(x => x.IsEnabledAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact(DisplayName = "Should_Return_404_When_EnabledWhen_Feature_Flag_Is_Disabled")]
    public async Task Should_Return_404_When_EnabledWhen_Feature_Flag_Is_Disabled()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("NewRequestFlow"))
            .ReturnsAsync(false);

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var nextMiddlewareCalled = false;
        RequestDelegate next = _ =>
        {
            nextMiddlewareCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new FeatureFlagMiddleware(next, featureManagerMock.Object, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/requests";

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new EnabledByFeatureFlagMetadata("NewRequestFlow")
            ),
            "TestEndpoint"
        );

        httpContext.SetEndpoint(endpoint);

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        Assert.False(nextMiddlewareCalled);
    }

    [Fact(DisplayName = "Should_Continue_When_EnabledWhen_Feature_Flag_Is_Enabled")]
    public async Task Should_Continue_When_EnabledWhen_Feature_Flag_Is_Enabled()
    {
        // Arrange
        var featureManagerMock = new Mock<IFeatureManager>();
        featureManagerMock
            .Setup(x => x.IsEnabledAsync("NewRequestFlow"))
            .ReturnsAsync(true);

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<FeatureFlagMiddleware>();

        var nextMiddlewareCalled = false;
        RequestDelegate next = _ =>
        {
            nextMiddlewareCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new FeatureFlagMiddleware(next, featureManagerMock.Object, logger);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/requests";

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new EnabledByFeatureFlagMetadata("NewRequestFlow")
            ),
            "TestEndpoint"
        );

        httpContext.SetEndpoint(endpoint);

        // Act
        await middleware.InvokeAsync(httpContext, new RecordingAuditLogger());

        // Assert
        Assert.True(nextMiddlewareCalled);
        Assert.NotEqual(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
    }
}

