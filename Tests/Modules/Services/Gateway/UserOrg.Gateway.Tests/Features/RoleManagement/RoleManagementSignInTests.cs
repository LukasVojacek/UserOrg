using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Startup;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

/// <summary>
/// Pins the role-management console's shared-token sign-in (ADR-0110): the <c>?t=</c> exchange for the
/// console cookie, the 404 camouflage for a missing or wrong token, the signed-in and signed-out audit
/// records, and the options that refuse to start without a token. The real gateway wiring runs in an
/// in-memory server.
/// </summary>
public sealed class RoleManagementSignInTests
{
    private const string AccessToken = "role-management-test-token-0123456789";

    private const string CookieName = "RoleManagementAuth";

    private const string ConsolePath = "/role-management";

    private const string WhoAmIPath = "/role-management/test-whoami";

    private const string AntiforgeryPath = "/role-management/test-antiforgery";

    private const string SignOutPath = "/role-management/signout";

    private const string CircuitProbePath = "/_blazor/test-probe";

    private const string SignedInAction = "gateway.role_management.signed_in";

    private const string SignedOutAction = "gateway.role_management.signed_out";

    private static readonly string OperatorId = $"rm-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AccessToken)))[..16]}";

    [Fact]
    public async Task Should_AnswerNotFoundAndWriteNothing_When_AConsoleRequestHasNeitherCookieNorToken()
    {
        await using var host = await ConsoleHost.StartAsync();

        var response = await host.GetAsync(ConsolePath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(host.HasCookie(CookieName));
        Assert.Empty(host.Audit.Writes);
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData(AccessToken + "x")]
    [InlineData("role-management-test-token-012345678")]
    [InlineData("ROLE-MANAGEMENT-TEST-TOKEN-0123456789")]
    public async Task Should_AnswerNotFoundAndRecordDenied_When_TheTokenIsWrong(string token)
    {
        await using var host = await ConsoleHost.StartAsync();

        var response = await host.GetAsync($"{ConsolePath}?t={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(host.HasCookie(CookieName));
        var record = SingleRecord(host, SignedInAction);
        Assert.Equal(AuditOutcome.Denied, record.Outcome);
        Assert.Equal("gateway.role_management_sign_in_failed", record.Reason);
        Assert.Equal("anonymous", record.ActorId);
        Assert.Null(record.Data);
    }

    [Fact]
    public async Task Should_IssueAStrictConsoleCookieAndRedirectWithoutTheToken_When_TheTokenIsRight()
    {
        await using var host = await ConsoleHost.StartAsync();

        var response = await host.SignInAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ConsolePath, response.Headers.Location!.OriginalString);
        var cookie = host.LastSetCookie(CookieName)!;
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.True(cookie.Secure);
        Assert.True(cookie.HttpOnly);

        var whoAmI = await host.WhoAmIAsync();
        Assert.Equal(OperatorId, whoAmI["sub"]);
        Assert.Equal("True", whoAmI["is_admin"]);
        Assert.False(string.IsNullOrEmpty(whoAmI["rm_access_id"]));

        var record = SingleRecord(host, SignedInAction);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal(OperatorId, record.ActorId);
        Assert.Equal("user", record.ActorType);
        Assert.Null(record.Reason);
        Assert.Equal(whoAmI["rm_access_id"], record.Data![Constants.RoleManagementAccessIdClaim]);
    }

    [Fact]
    public async Task Should_KeepTheTokenOutOfTheAuditRecord_When_TheOperatorSignsIn()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.SignInAsync();

        var record = SingleRecord(host, SignedInAction);
        Assert.DoesNotContain(AccessToken, record.ActorId, StringComparison.Ordinal);
        Assert.DoesNotContain(record.Data!.Values, value => value?.ToString()?.Contains(AccessToken, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Should_GiveEachSignInItsOwnAccessId_When_TheTokenIsUsedTwice()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.SignInAsync();
        var first = (await host.WhoAmIAsync())["rm_access_id"];
        host.ClearCookies();
        await host.SignInAsync();
        var second = (await host.WhoAmIAsync())["rm_access_id"];

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Should_IssueNoCookie_When_TheSignedInRecordCannotBeWritten()
    {
        await using var host = await ConsoleHost.StartAsync();
        host.Audit.ThrowOnWrite = new InvalidOperationException("audit sink down");

        await Record.ExceptionAsync(() => host.SignInAsync());

        Assert.False(host.HasCookie(CookieName));
        host.Audit.ThrowOnWrite = null;
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync(WhoAmIPath)).StatusCode);
    }

    [Fact]
    public async Task Should_LetTheCookieInWithoutTheToken_When_TheOperatorHasSignedIn()
    {
        await using var host = await ConsoleHost.StartAsync();
        await host.SignInAsync();

        var response = await host.GetAsync(WhoAmIPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(host.Audit.Writes);
    }

    [Fact]
    public async Task Should_AnswerUnauthorized_When_TheCookieIsPresentButInvalid()
    {
        await using var host = await ConsoleHost.StartAsync();
        host.SetCookie(CookieName, "not-a-valid-ticket");

        var response = await host.GetAsync(ConsolePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(host.Audit.Writes);
    }

    [Fact]
    public async Task Should_AnswerUnauthorizedInsteadOfRedirecting_When_ACircuitRequestHasNoCookie()
    {
        await using var host = await ConsoleHost.StartAsync();

        var response = await host.GetAsync(CircuitProbePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Should_RedirectUnderThePathBase_When_TheGatewayIsHostedUnderOne()
    {
        await using var host = await ConsoleHost.StartAsync(new Dictionary<string, string?> { ["Gateway:PathBase"] = "/userorg" }, pathBase: "/userorg");

        var response = await host.GetAsync($"/userorg{ConsolePath}?t={AccessToken}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/userorg{ConsolePath}", response.Headers.Location!.OriginalString);
        Assert.True(host.HasCookie(CookieName));
    }

    [Fact]
    public async Task Should_EndTheSessionAndRecordIt_When_TheOperatorSignsOut()
    {
        await using var host = await ConsoleHost.StartAsync();
        await host.SignInAsync();

        var response = await host.SignOutAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(host.HasCookie(CookieName));
        var record = SingleRecord(host, SignedOutAction);
        Assert.Equal(AuditOutcome.Success, record.Outcome);
        Assert.Equal(OperatorId, record.ActorId);
        Assert.Null(record.Reason);
        Assert.Equal(HttpStatusCode.NotFound, (await host.GetAsync(WhoAmIPath)).StatusCode);
    }

    [Fact]
    public async Task Should_StillSignOutWithoutAServerError_When_TheSignedOutRecordCannotBeWritten()
    {
        await using var host = await ConsoleHost.StartAsync();
        await host.SignInAsync();
        var antiforgeryToken = await host.GetAntiforgeryTokenAsync();
        host.Audit.ThrowOnWrite = new InvalidOperationException("audit sink down");

        var response = await host.SignOutAsync(antiforgeryToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(host.HasCookie(CookieName));
    }

    [Fact]
    public async Task Should_RefuseTheSignOutAndKeepTheSession_When_TheAntiforgeryTokenIsMissing()
    {
        await using var host = await ConsoleHost.StartAsync();
        await host.SignInAsync();

        var response = await host.SendAsync(new HttpRequestMessage(HttpMethod.Post, SignOutPath)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>()),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(host.HasCookie(CookieName));
        Assert.DoesNotContain(host.Audit.Writes, w => w.Event.Action == SignedOutAction);
    }

    [Fact]
    public async Task Should_WriteNoIssuedCookieLog_When_TheOperatorSignsIn()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.SignInAsync();

        Assert.Empty(MiddlewareLogs(host, "Issued role management cookie"));
    }

    [Fact]
    public async Task Should_LogTheExpiredCookieAtDebug_When_TheCookieIsPresentButInvalid()
    {
        await using var host = await ConsoleHost.StartAsync();
        host.SetCookie(CookieName, "not-a-valid-ticket");

        await host.GetAsync(ConsolePath);

        Assert.Equal(LogLevel.Debug, SingleMiddlewareLog(host, "Role management cookie expired or invalid").Level);
    }

    [Fact]
    public async Task Should_KeepTheDenialAtWarning_When_AConsoleRequestHasNeitherCookieNorToken()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.GetAsync(ConsolePath);

        Assert.Equal(LogLevel.Warning, SingleMiddlewareLog(host, "without token").Level);
    }

    [Fact]
    public async Task Should_KeepTheDenialAtWarning_When_TheTokenIsWrong()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.GetAsync($"{ConsolePath}?t=wrong-token");

        Assert.Equal(LogLevel.Warning, SingleMiddlewareLog(host, "with invalid token").Level);
    }

    [Fact]
    public async Task Should_WriteNoPathCheck_When_AConsoleRequestArrives()
    {
        await using var host = await ConsoleHost.StartAsync();

        await host.GetAsync(ConsolePath);

        Assert.Empty(MiddlewareLogs(host, "RoleManagement path check"));
    }

    [Fact]
    public void Should_AcceptTheOptions_When_TheTokenIsConfigured()
    {
        var options = ResolveOptions(new Dictionary<string, string?>());

        Assert.Equal(AccessToken, options.AccessToken);
        Assert.Equal(CookieName, options.CookieName);
    }

    [Theory]
    [InlineData("RoleManagement:AccessToken", "")]
    [InlineData("RoleManagement:AccessToken", "   ")]
    [InlineData("RoleManagement:CookieExpirationMinutes", "0")]
    [InlineData("RoleManagement:CookieName", "")]
    public void Should_RejectTheOptions_When_ASettingIsMissingOrOutOfRange(string key, string value)
    {
        Assert.Throws<OptionsValidationException>(() => ResolveOptions(new Dictionary<string, string?> { [key] = value }));
    }

    [Fact]
    public void Should_StartWithoutAnyIdpClientSetting_When_OnlyTheTokenIsConfigured()
    {
        var options = ResolveOptions(new Dictionary<string, string?>
        {
            ["RoleManagement:Oidc:ClientId"] = null,
            ["RoleManagement:Oidc:ClientSecret"] = null,
        });

        Assert.Equal(AccessToken, options.AccessToken);
    }

    private static RoleManagementOptions ResolveOptions(IDictionary<string, string?> overrides)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ConsoleHost.BaseConfiguration.Concat(overrides).GroupBy(p => p.Key).Select(g => g.Last()))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRoleManagementFeature(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<RoleManagementOptions>>().Value;
    }

    private static AuditEvent SingleRecord(ConsoleHost host, string action)
        => Assert.Single(host.Audit.Writes.Select(w => w.Event), e => e.Action == action);

    private static CapturedLogEntry SingleMiddlewareLog(ConsoleHost host, string messageFragment)
        => Assert.Single(MiddlewareLogs(host, messageFragment));

    private static IReadOnlyList<CapturedLogEntry> MiddlewareLogs(ConsoleHost host, string messageFragment)
        => [.. host.Logs.From<RoleManagementAuthMiddleware>().Where(e => e.Message.Contains(messageFragment, StringComparison.Ordinal))];

    private sealed class ConsoleHost : IAsyncDisposable
    {
        public static readonly IReadOnlyDictionary<string, string?> BaseConfiguration = new Dictionary<string, string?>
        {
            ["Idp:Authority"] = "https://idp.test/realms/master",
            ["Idp:Audience"] = "userorg",
            ["RoleManagement:AccessToken"] = AccessToken,
            ["RoleManagement:SeedFilePath"] = "/nonexistent/rbac/seed.json",
        };

        private readonly WebApplication _app;

        private readonly HttpClient _client;

        private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);

        private readonly Dictionary<string, SetCookieHeaderValue> _lastSetCookies = new(StringComparer.Ordinal);

        private ConsoleHost(WebApplication app)
        {
            _app = app;
            Audit = app.Services.GetRequiredService<RecordingAuditEventSink>();
            Logs = app.Services.GetRequiredService<CapturingLoggerProvider>();
            _client = app.GetTestClient();
            _client.BaseAddress = new Uri("https://localhost/");
        }

        public RecordingAuditEventSink Audit { get; }

        public CapturingLoggerProvider Logs { get; }

        public static async Task<ConsoleHost> StartAsync(IDictionary<string, string?>? overrides = null, string? pathBase = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<CapturingLoggerProvider>();
            builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<CapturingLoggerProvider>());
            builder.Logging.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);
            builder.Configuration.AddInMemoryCollection(
                BaseConfiguration.Concat(overrides ?? new Dictionary<string, string?>()).GroupBy(p => p.Key).Select(g => g.Last()));

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<RecordingAuditEventSink>();
            builder.Services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));

            var hostedBefore = builder.Services.Where(d => d.ServiceType == typeof(IHostedService)).ToHashSet();
            builder.Services.AddRoleManagementFeature(builder.Configuration);
            builder.AddAuthenticationServices();
            foreach (var hosted in builder.Services.Where(d => d.ServiceType == typeof(IHostedService) && !hostedBefore.Contains(d)).ToList())
            {
                builder.Services.Remove(hosted);
            }

            builder.Services.AddAuthorization(options => options.AddPolicy(Constants.RoleManagementAccessPolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(Constants.RoleManagementAccessIdClaim);
            }));

            var app = builder.Build();
            if (pathBase is not null)
            {
                app.UsePathBase(pathBase);
            }

            app.UseAuthentication();
            app.UseRoleManagementAuth();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapRoleManagementFeature();
            app.MapGet(WhoAmIPath, (HttpContext context) => Results.Json(new Dictionary<string, string?>
            {
                ["sub"] = context.User.FindFirstValue("sub"),
                ["rm_access_id"] = context.User.FindFirstValue(Constants.RoleManagementAccessIdClaim),
                ["is_admin"] = context.User.IsInRole(RoleInfoConstants.PlatformAdministratorRoleName).ToString(),
            })).RequireAuthorization(Constants.RoleManagementAccessPolicyName);
            app.MapGet(CircuitProbePath, () => Results.Ok()).RequireAuthorization(Constants.RoleManagementAccessPolicyName);
            app.MapGet(AntiforgeryPath, (HttpContext context, IAntiforgery antiforgery) => antiforgery.GetAndStoreTokens(context).RequestToken)
                .RequireAuthorization(Constants.RoleManagementAccessPolicyName);

            await app.StartAsync();
            return new ConsoleHost(app);
        }

        public bool HasCookie(string name) => _cookies.ContainsKey(name);

        public SetCookieHeaderValue? LastSetCookie(string name) => _lastSetCookies.GetValueOrDefault(name);

        public void SetCookie(string name, string value) => _cookies[name] = value;

        public void ClearCookies() => _cookies.Clear();

        public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(new HttpRequestMessage(HttpMethod.Get, path));

        public Task<HttpResponseMessage> SignInAsync() => GetAsync($"{ConsolePath}?t={AccessToken}");

        public async Task<Dictionary<string, string?>> WhoAmIAsync()
        {
            var response = await GetAsync(WhoAmIPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<Dictionary<string, string?>>())!;
        }

        public async Task<string> GetAntiforgeryTokenAsync()
        {
            var response = await GetAsync(AntiforgeryPath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<HttpResponseMessage> SignOutAsync(string? antiforgeryToken = null)
        {
            antiforgeryToken ??= await GetAntiforgeryTokenAsync();
            return await SendAsync(new HttpRequestMessage(HttpMethod.Post, SignOutPath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = antiforgeryToken }),
            });
        }

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add(HeaderNames.Cookie, string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}")));
            }

            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var setCookies))
            {
                foreach (var setCookie in SetCookieHeaderValue.ParseList(setCookies.ToList()))
                {
                    var name = setCookie.Name.ToString();
                    _lastSetCookies[name] = setCookie;
                    if (setCookie.Value.Length == 0 || setCookie.Expires < DateTimeOffset.UtcNow)
                    {
                        _cookies.Remove(name);
                        continue;
                    }

                    _cookies[name] = setCookie.Value.ToString();
                }
            }

            return response;
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.DisposeAsync();
        }
    }
}
