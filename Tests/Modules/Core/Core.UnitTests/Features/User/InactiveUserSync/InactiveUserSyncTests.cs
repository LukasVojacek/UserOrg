using System.Net;

using Core.DataAccess.Contexts;
using Core.DataAccess.Events;

using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;

using Moq;

using InactiveUserSyncService = Core.Features.User.InactiveUserSync.InactiveUserSync;
using InactiveUserSyncOptions = Core.Features.User.InactiveUserSync.InactiveUserSyncOptions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.UnitTests.Features.User.InactiveUserSync;

/// <summary>
/// Unit tests for <see cref="InactiveUserSyncService"/> background service.
/// Tests cover the token + disabled-users HTTP path, parsing of the user_id="..." lines and the
/// SyncOnceAsync error handling.
/// </summary>
public sealed class InactiveUserSyncTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class TestDelegatingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            await handler(request);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "") => client;
    }

    private const string TokenJson = """{"access_token":"test-token","expires_in":3600,"token_type":"Bearer"}""";

    private static InactiveUserSyncOptions CreateDefaultOptions() =>
        new()
        {
            BaseUrl = "https://aai.test.local/auth/realms/LEXIS",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            SyncInterval = TimeSpan.FromDays(7),
            Enabled = true,
        };

    private static (InactiveUserSyncService Service, CapturingLogger<InactiveUserSyncService> Logger) CreateService(
        IHttpClientFactory httpClientFactory)
    {
        var logger = new CapturingLogger<InactiveUserSyncService>();
        var provider = new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider();
        var service = ActivatorUtilities.CreateInstance<InactiveUserSyncService>(
            provider,
            provider.GetRequiredService<IServiceScopeFactory>(),
            httpClientFactory,
            Options.Create(CreateDefaultOptions()),
            logger);
        return (service, logger);
    }

    private static IHttpClientFactory CreateHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new StaticHttpClientFactory(new HttpClient(new TestDelegatingHandler(request => Task.FromResult(respond(request)))));

    private static IHttpClientFactory CreateHttpClientFactory(string tokenResponse, string disabledUsersResponse) =>
        CreateHttpClientFactory(request =>
        {
            var isTokenRequest = request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(isTokenRequest ? tokenResponse : disabledUsersResponse),
            };
        });

    [Fact]
    public void Should_ParseSingleUserGuid_When_ValidFormat()
    {
        var userId = Guid.NewGuid();
        var text = $"""user_id="{userId}" """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Single(result);
        Assert.Contains(userId, result);
    }

    [Fact]
    public void Should_ParseMultipleUserGuids_When_ValidFormat()
    {
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var userId3 = Guid.NewGuid();
        var text = $"""
            user_id="{userId1}"
            user_id="{userId2}"
            user_id="{userId3}"
            """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Equal(3, result.Count);
        Assert.Contains(userId1, result);
        Assert.Contains(userId2, result);
        Assert.Contains(userId3, result);
    }

    [Fact]
    public void Should_ReturnEmpty_When_NoValidGuids()
    {
        var text = """
            some_id="not-a-guid"
            another_field="also-not-a-guid"
            """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Empty(result);
    }

    [Fact]
    public void Should_ReturnEmpty_When_NullOrWhitespace()
    {
        Assert.Empty(InactiveUserSyncService.ParseInvalidUserIds(null!));
        Assert.Empty(InactiveUserSyncService.ParseInvalidUserIds(""));
        Assert.Empty(InactiveUserSyncService.ParseInvalidUserIds("   "));
    }

    [Fact]
    public void Should_SkipMalformedLines_When_MixedContent()
    {
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var text = $"""
            user_id="{userId1}"
            malformed_line
            user_id="{userId2}"
            another_bad_line="value"
            """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Equal(2, result.Count);
        Assert.Contains(userId1, result);
        Assert.Contains(userId2, result);
    }

    [Fact]
    public void Should_ParseGuids_When_WithWhitespace()
    {
        var userId = Guid.NewGuid();
        var text = $"""   user_id="{userId}"   """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Single(result);
        Assert.Contains(userId, result);
    }

    [Fact]
    public void Should_ReturnEmpty_When_InvalidGuidFormat()
    {
        var text = """user_id="not-a-valid-guid" """;

        var result = InactiveUserSyncService.ParseInvalidUserIds(text);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Should_ReturnParsedUserIds_When_FetchSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        var disabledUsersResponse = $"""
            user_id="{userId1}"
            user_id="{userId2}"
            """;

        var (service, _) = CreateService(CreateHttpClientFactory(TokenJson, disabledUsersResponse));

        var result = await service.FetchInvalidUserIdsAsync(ct);

        Assert.Equal(2, result.Count);
        Assert.Contains(userId1, result);
        Assert.Contains(userId2, result);
    }

    [Fact]
    public async Task Should_SendBearerToken_When_TokenJsonContainsWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        const string tokenJson = """
            {
              "token_type" : "Bearer",
              "access_token" : "spaced-token",
              "expires_in" : 300
            }
            """;
        string? sentAuthorization = null;

        var factory = CreateHttpClientFactory(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tokenJson) };

            sentAuthorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"""user_id="{userId}" """),
            };
        });
        var (service, _) = CreateService(factory);

        var result = await service.FetchInvalidUserIdsAsync(ct);

        Assert.Equal("Bearer spaced-token", sentAuthorization);
        Assert.Equal([userId], result);
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationException_When_TokenEndpointRejectsClient()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, logger) = CreateService(
            CreateHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchInvalidUserIdsAsync(ct));

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Failed to obtain access token"));
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationException_When_TokenResponseHasNoAccessToken()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _) = CreateService(
            CreateHttpClientFactory("""{"expires_in":3600,"token_type":"Bearer"}""", ""));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.FetchInvalidUserIdsAsync(ct));
    }

    [Fact]
    public async Task Should_ThrowHttpRequestException_When_DisabledUsersEndpointFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = CreateHttpClientFactory(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/protocol/openid-connect/token")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(TokenJson) }
                : new HttpResponseMessage(HttpStatusCode.Forbidden));
        var (service, _) = CreateService(factory);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => service.FetchInvalidUserIdsAsync(ct));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Should_ReturnEmpty_When_NoUsersFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _) = CreateService(CreateHttpClientFactory(TokenJson, ""));

        var result = await service.FetchInvalidUserIdsAsync(ct);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Should_LogAndReturn_When_NoInactiveUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, logger) = CreateService(CreateHttpClientFactory(TokenJson, ""));

        await service.SyncOnceAsync(ct);

        Assert.Contains(logger.Entries, e => e.Message.Contains("No inactive users to process"));
    }

    [Fact]
    public async Task Should_LogErrorAndContinue_When_NetworkErrorOccurs()
    {
        var ct = TestContext.Current.CancellationToken;
        var factory = new StaticHttpClientFactory(new HttpClient(new TestDelegatingHandler(_ =>
            throw new HttpRequestException("Network error"))));
        var (service, logger) = CreateService(factory);

        await service.SyncOnceAsync(ct);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Should_LogCancellationAndReturn_When_OperationCancelled()
    {
        var factory = new StaticHttpClientFactory(new HttpClient(new TestDelegatingHandler(_ =>
            throw new OperationCanceledException("Operation cancelled"))));
        var (service, logger) = CreateService(factory);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await service.SyncOnceAsync(cts.Token);

        Assert.Contains(logger.Entries, e => e.Message.Contains("cancelled during shutdown"));
    }

    // ========================================================================================
    // TESTS: SyncOnceAsync end to end against the EF in-memory DatabaseContext
    // ========================================================================================

    private static ServiceProvider BuildDatabaseServiceProvider()
    {
        var roleService = new Mock<IRoleService>();
        roleService.Setup(s => s.ListRoleNames(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<int, string>, Error>.Success(
                new Dictionary<int, string> { { 1, "User" }, { 2, "Admin" } }));

        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DomainEventChannel>();
        services.AddSingleton(roleService.Object);
        services.Configure<Core.Domain.User.WellKnownRoles>(_ => { });
        services.AddDbContext<DatabaseContext>(options => options.UseInMemoryDatabase(dbName));
        services.AddScoped<IRepository<Domain.User.User, IdpId>>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DatabaseContext>());
        return services.BuildServiceProvider();
    }

    private static async Task SeedUsersAsync(ServiceProvider sp, CancellationToken ct, params Guid[] keycloakIds)
    {
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        var repository = (IRepository<Domain.User.User, IdpId>)context;
        foreach (var id in keycloakIds)
        {
            await repository.AddAsync(
                Domain.User.User.CreateUser(
                    Email.Create($"{id:N}@example.com"),
                    UserName.Create($"u{id:N}"),
                    IdpId.Create(id.ToString())),
                ct);
        }
        await context.SaveChangesAsync(ct);
    }

    private static async Task<Dictionary<string, bool>> ReadDeletedFlagsAsync(ServiceProvider sp, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        return await context.Set<Domain.User.User>()
            .IgnoreQueryFilters()
            .ToDictionaryAsync(u => u.IdentityProviderId.Value, u => u.IsDeleted, ct);
    }

    private static (InactiveUserSyncService Service, CapturingLogger<InactiveUserSyncService> Logger) CreateDatabaseService(
        ServiceProvider sp,
        Guid disabledUser)
    {
        var logger = new CapturingLogger<InactiveUserSyncService>();
        var service = ActivatorUtilities.CreateInstance<InactiveUserSyncService>(
            sp,
            sp.GetRequiredService<IServiceScopeFactory>(),
            CreateHttpClientFactory(TokenJson, $"""user_id="{disabledUser}" """),
            Options.Create(CreateDefaultOptions()),
            logger);
        return (service, logger);
    }

    [Fact]
    public async Task Should_DeactivateOnlyReportedUser_AndDispatchUserDeletedEvent_When_SyncRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var staysActive = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        await using var sp = BuildDatabaseServiceProvider();
        await SeedUsersAsync(sp, ct, staysActive, disabled);
        var (service, _) = CreateDatabaseService(sp, disabled);

        await service.SyncOnceAsync(ct);

        var flags = await ReadDeletedFlagsAsync(sp, ct);
        Assert.False(flags[staysActive.ToString()]);
        Assert.True(flags[disabled.ToString()]);

        var channel = sp.GetRequiredService<DomainEventChannel>();
        Assert.True(channel.Reader.TryRead(out var dispatched));
        var deleted = Assert.IsType<UserDeletedEvent>(dispatched);
        Assert.Equal(disabled.ToString(), deleted.IdentityProviderId.Value);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Should_SkipAlreadyDeactivatedUser_When_SyncRunsAgain()
    {
        var ct = TestContext.Current.CancellationToken;
        var staysActive = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        await using var sp = BuildDatabaseServiceProvider();
        await SeedUsersAsync(sp, ct, staysActive, disabled);
        var (first, _) = CreateDatabaseService(sp, disabled);
        await first.SyncOnceAsync(ct);
        var channel = sp.GetRequiredService<DomainEventChannel>();
        Assert.True(channel.Reader.TryRead(out _));

        var (second, logger) = CreateDatabaseService(sp, disabled);
        await second.SyncOnceAsync(ct);

        var flags = await ReadDeletedFlagsAsync(sp, ct);
        Assert.False(flags[staysActive.ToString()]);
        Assert.True(flags[disabled.ToString()]);
        Assert.False(channel.Reader.TryRead(out _));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Message.Contains("0 marked as deleted, 1 skipped"));
    }
}
