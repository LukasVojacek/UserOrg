using System.Diagnostics;
using System.Net;

using Heappe.Contracts;
using Heappe.Features.Connection;
using Heappe.Features.Nexus;

using Lukdrasil.Result;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Polly.CircuitBreaker;
using Polly.Timeout;

using TokenDelegation.Models;

using UserOrg.Shared;

using Xunit;

using static Heappe.UnitTests.Features.Connection.TokenExchangeValues;

namespace Heappe.UnitTests.Features.Connection;

/// <summary>
/// The per-call token exchange in front of every user call to a HEAppE instance (ADR-0118). It is registered only
/// when the <c>TokenExchangeEnabled</c> flag is on at startup, so it never checks the flag itself: the caller's token
/// is always exchanged for one whose audience is the Keycloak client Nexus lists for the instance, cached by a hash
/// of the caller's token for at most 300 s, and every failure is a <see cref="Result{TValue, TError}"/> failure with
/// a stable cause code and a rate-limited Warning that carries no token, secret or IdP text. The flag-off behaviour
/// is <see cref="PassThroughTokenExchangerTests"/>. The delegation service, the clock and the logger are fakes.
/// </summary>
public sealed class HeappeTokenExchangerTests : IDisposable
{
    private static readonly HeappeIdentifier HeappeMock = new("heappe-mock");
    private static readonly HeappeIdentifier OtherInstance = new("heappe-other");

    private readonly FakeTokenDelegationService _delegation = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly LogSink _logs = new();
    private readonly RecordingMemoryCache _cache;

    public HeappeTokenExchangerTests() => _cache = new RecordingMemoryCache(_time);

    public void Dispose() => _cache.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Flag_and_delegation_client_names_are_the_configured_ones()
    {
        Assert.Equal("heappe-token-exchange", TokenExchangeOptions.DelegationClientName);
    }

    [Fact]
    public void Failure_cause_codes_are_stable_strings()
    {
        Assert.Equal("token-exchange-unavailable", HeappeFailureCause.TokenExchangeUnavailable);
        Assert.Equal("token-exchange-failed", HeappeFailureCause.TokenExchangeFailed);
    }

    [Fact]
    public async Task Flag_on_returns_the_exchanged_token()
    {
        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.True(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
        Assert.Equal(ExchangedToken, result.Value);
    }

    [Fact]
    public async Task Flag_on_exchanges_through_the_delegation_client_with_the_route_audience_and_scopes()
    {
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        var call = Assert.Single(_delegation.Calls);
        Assert.Equal(DelegationClient, call.ClientName);
        Assert.Equal(CallerToken, call.SubjectToken);
        Assert.NotNull(call.Options);
        Assert.Equal(HeappeClientId, call.Options.Audience);
        Assert.Equal("openid heappe", call.Options.Scope);
    }

    [Fact]
    public async Task Null_client_id_is_token_exchange_unavailable()
    {
        await AssertUnavailable(Route(clientId: null));
    }

    [Fact]
    public async Task Empty_scopes_are_token_exchange_unavailable()
    {
        await AssertUnavailable(Route(scopes: []));
    }

    [Theory]
    [InlineData("open id")]
    [InlineData("heappe\t")]
    [InlineData("quote\"d")]
    [InlineData("back\\slash")]
    [InlineData("")]
    public async Task A_scope_that_is_not_an_rfc6749_scope_token_is_token_exchange_unavailable(string scope)
    {
        await AssertUnavailable(Route(scopes: ["openid", scope]));
    }

    [Theory]
    [InlineData("heappe mock")]
    [InlineData(" heappe-mock-client")]
    [InlineData("heappe-mock-client\n")]
    public async Task A_client_id_with_whitespace_is_token_exchange_unavailable(string clientId)
    {
        await AssertUnavailable(Route(clientId: clientId));
    }

    [Fact]
    public async Task A_throwing_exchange_is_token_exchange_failed()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.AccessDenied, DelegationClient, "access denied"));

        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        AssertFailed(result, nameof(TokenExchangeException));
    }

    [Fact]
    public async Task An_attempt_timeout_is_token_exchange_failed()
    {
        _delegation.Throws(new TimeoutRejectedException("The operation didn't complete within the allowed timeout of '00:00:05'."));

        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        AssertFailed(result, nameof(TimeoutRejectedException));
    }

    [Fact]
    public async Task A_timeout_cancellation_while_the_caller_still_waits_is_token_exchange_failed()
    {
        using var timeout = new CancellationTokenSource();
        await timeout.CancelAsync();
        _delegation.Throws(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", null, timeout.Token));

        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        AssertFailed(result, nameof(TaskCanceledException));
    }

    [Fact]
    public async Task An_open_breaker_is_token_exchange_failed()
    {
        _delegation.Throws(new BrokenCircuitException("The circuit is now open and is not allowing calls."));

        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        AssertFailed(result, nameof(BrokenCircuitException));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_missing_client_id_is_token_exchange_failed(string? clientId)
    {
        var options = ConfiguredOptions();
        options.ClientId = clientId;

        await AssertMisconfigured(options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task A_missing_client_secret_is_token_exchange_failed(string? clientSecret)
    {
        var options = ConfiguredOptions();
        options.ClientSecret = clientSecret;

        await AssertMisconfigured(options);
    }

    [Fact]
    public async Task A_missing_authority_is_token_exchange_failed()
    {
        var options = ConfiguredOptions();
        options.Authority = null;

        await AssertMisconfigured(options);
    }

    private async Task AssertMisconfigured(TokenExchangeOptions options)
    {
        var result = await Exchanger(options: options).ExchangeAsync(Route(), CallerToken, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Equal(State.Error, result.State);
        Assert.Empty(_delegation.Calls);
        Assert.Contains("heappe-mock", result.Error.Description, StringComparison.Ordinal);
        AssertCarriesNoSecret(result.Error.Description);
    }

    [Theory]
    [InlineData("token-exchange")]
    [InlineData("http")]
    [InlineData("timeout")]
    [InlineData("task-canceled")]
    [InlineData("breaker")]
    [InlineData("invalid-operation")]
    [InlineData("unexpected")]
    public async Task Qs05_every_exchange_failure_is_a_failure_result_and_never_an_unhandled_exception(string failure)
    {
        _delegation.Throws(FailureOf(failure));

        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
            Assert.False(result.IsSuccess);
            Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task Caller_cancellation_during_the_exchange_is_rethrown()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _delegation.Respond = async (_, _) =>
        {
            await caller.CancelAsync();
            throw new OperationCanceledException(caller.Token);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Exchanger().ExchangeAsync(Route(), CallerToken, caller.Token));
        Assert.DoesNotContain(_logs.Records, r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Qs03_a_repeated_call_with_the_same_token_client_and_scopes_is_served_from_the_cache()
    {
        var first = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        var second = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.Equal(ExchangedToken, first.Value);
        Assert.Equal(ExchangedToken, second.Value);
        Assert.Single(_delegation.Calls);
    }

    [Fact]
    public async Task The_cache_key_is_scope_sensitive()
    {
        await Exchanger().ExchangeAsync(Route(scopes: ["openid", "heappe"]), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(scopes: ["openid", "heappe", "offline_access"]), CallerToken, Ct);

        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task The_cache_key_sorts_the_scopes()
    {
        await Exchanger().ExchangeAsync(Route(scopes: ["openid", "heappe"]), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(scopes: ["heappe", "openid"]), CallerToken, Ct);

        Assert.Single(_delegation.Calls);
    }

    [Fact]
    public async Task The_cache_key_is_client_sensitive()
    {
        await Exchanger().ExchangeAsync(Route(clientId: HeappeClientId), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(clientId: "another-heappe-client"), CallerToken, Ct);

        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task The_cache_key_is_token_sensitive()
    {
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(), "another-caller-token", Ct);

        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task A_token_without_expires_in_is_not_cached()
    {
        _delegation.ExpiresIn(null);

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(30)]
    public async Task A_token_expiring_within_30_seconds_is_not_cached(int expiresInSeconds)
    {
        _delegation.ExpiresIn(TimeSpan.FromSeconds(expiresInSeconds));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task The_cache_lifetime_is_expires_in_minus_30_seconds()
    {
        _delegation.ExpiresIn(TimeSpan.FromSeconds(120));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _time.Advance(TimeSpan.FromSeconds(89));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Single(_delegation.Calls);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task The_cache_lifetime_is_capped_at_300_seconds()
    {
        _delegation.ExpiresIn(TimeSpan.FromHours(1));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _time.Advance(TimeSpan.FromSeconds(299));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Single(_delegation.Calls);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task A_failed_exchange_is_not_cached()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp down"));
        var failed = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        _delegation.ExpiresIn(TimeSpan.FromHours(1));
        var retried = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.False(failed.IsSuccess);
        Assert.True(retried.IsSuccess);
        Assert.Equal(ExchangedToken, retried.Value);
        Assert.Equal(2, _delegation.Calls.Count);
    }

    [Fact]
    public async Task Qs07_a_user_disabled_after_an_exchange_gets_no_token_for_heappe_after_300_seconds()
    {
        _delegation.ExpiresIn(TimeSpan.FromHours(1));
        var beforeDisable = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.AccessDenied, DelegationClient, "user disabled") { StatusCode = HttpStatusCode.BadRequest, Error = "access_denied" });

        _time.Advance(TimeSpan.FromSeconds(301));
        var afterDisable = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.Equal(ExchangedToken, beforeDisable.Value);
        Assert.False(afterDisable.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, afterDisable.Error.Code);
    }

    [Fact]
    public async Task Qs02_cache_keys_carry_no_token_or_secret()
    {
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _delegation.Throws(SecretBearingFailure());
        await Exchanger().ExchangeAsync(Route(), "second-caller-token", Ct);

        Assert.NotEmpty(_cache.Keys);
        foreach (var key in _cache.Keys.Select(k => k.ToString() ?? string.Empty))
        {
            AssertCarriesNoSecret(key);
            Assert.DoesNotContain("second-caller-token", key, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Qs02_a_failed_exchange_description_names_the_instance_and_exception_type_but_no_token_secret_or_idp_text()
    {
        _delegation.Throws(SecretBearingFailure());

        var result = await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.False(result.IsSuccess);
        Assert.Contains("heappe-mock", result.Error.Description, StringComparison.Ordinal);
        Assert.Contains(nameof(TokenExchangeException), result.Error.Description, StringComparison.Ordinal);
        AssertCarriesNoSecret(result.Error.Description);
        Assert.DoesNotContain("IdP says", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Qs02_the_failure_warning_carries_no_token_secret_or_idp_text()
    {
        _delegation.Throws(SecretBearingFailure());

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        Assert.NotEmpty(_logs.Records);
        foreach (var record in _logs.Records)
        {
            AssertCarriesNoSecret(record.Text);
            Assert.DoesNotContain("IdP says", record.Text, StringComparison.Ordinal);
            Assert.Null(record.Exception);
        }
    }

    [Fact]
    public async Task Qs06_a_failure_logs_one_warning_with_identifier_cause_exception_type_kind_and_status_code()
    {
        var failure = new TokenExchangeException(TokenExchangeErrorKind.InvalidTarget, DelegationClient, "refused") { StatusCode = HttpStatusCode.BadRequest, Error = "invalid_target" };
        _delegation.Throws(failure);

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        var warning = Assert.Single(_logs.Warnings);
        Assert.Null(warning.Exception);
        Assert.Contains("heappe-mock", warning.Text, StringComparison.Ordinal);
        Assert.Contains(HeappeFailureCause.TokenExchangeFailed, warning.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(TokenExchangeException), warning.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(TokenExchangeErrorKind.InvalidTarget), warning.Text, StringComparison.Ordinal);
        Assert.Contains(failure.StatusCode.ToString()!, warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(_logs.Records, r => r.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Qs06_an_unavailable_exchange_logs_a_warning_with_identifier_and_cause()
    {
        await Exchanger().ExchangeAsync(Route(clientId: null), CallerToken, Ct);

        var warning = Assert.Single(_logs.Warnings);
        Assert.Contains("heappe-mock", warning.Text, StringComparison.Ordinal);
        Assert.Contains(HeappeFailureCause.TokenExchangeUnavailable, warning.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(_logs.Records, r => r.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task Qs06_the_failure_warning_is_logged_within_the_callers_trace()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp down"));
        using var request = new Activity("heappe-user-call");
        request.SetIdFormat(ActivityIdFormat.W3C).Start();

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);

        var warning = Assert.Single(_logs.Warnings);
        Assert.Equal(request.TraceId, warning.TraceId);
    }

    [Fact]
    public async Task Qs06_identical_warnings_are_limited_to_one_per_identifier_and_cause_per_minute_across_scopes()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp down"));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _time.Advance(TimeSpan.FromSeconds(30));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Single(_logs.Warnings);

        _time.Advance(TimeSpan.FromSeconds(2));
        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        Assert.Equal(2, _logs.Warnings.Count());
    }

    [Fact]
    public async Task Qs06_the_warning_limit_is_per_identifier()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp down"));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(identifier: OtherInstance), CallerToken, Ct);

        Assert.Equal(2, _logs.Warnings.Count());
        Assert.Contains(_logs.Warnings, w => w.Text.Contains("heappe-other", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Qs06_the_warning_limit_is_per_cause()
    {
        _delegation.Throws(new TokenExchangeException(TokenExchangeErrorKind.Transient, DelegationClient, "idp down"));

        await Exchanger().ExchangeAsync(Route(), CallerToken, Ct);
        await Exchanger().ExchangeAsync(Route(clientId: null), CallerToken, Ct);

        Assert.Equal(2, _logs.Warnings.Count());
        Assert.Contains(_logs.Warnings, w => w.Text.Contains(HeappeFailureCause.TokenExchangeUnavailable, StringComparison.Ordinal));
        Assert.Contains(_logs.Warnings, w => w.Text.Contains(HeappeFailureCause.TokenExchangeFailed, StringComparison.Ordinal));
    }

    private async Task AssertUnavailable(HeappeRoute route)
    {
        var result = await Exchanger().ExchangeAsync(route, CallerToken, Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeUnavailable, result.Error.Code);
        Assert.Equal(State.Error, result.State);
        Assert.Contains("heappe-mock", result.Error.Description, StringComparison.Ordinal);
        Assert.Empty(_delegation.Calls);
    }

    private static void AssertFailed(Result<string, Lukdrasil.Error.Error> result, string exceptionType)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(HeappeFailureCause.TokenExchangeFailed, result.Error.Code);
        Assert.Equal(State.Error, result.State);
        Assert.Contains("heappe-mock", result.Error.Description, StringComparison.Ordinal);
        Assert.Contains(exceptionType, result.Error.Description, StringComparison.Ordinal);
    }

    private static void AssertCarriesNoSecret(string text)
    {
        Assert.DoesNotContain(CallerToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(ExchangedToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntrospectionSecret, text, StringComparison.Ordinal);
    }

    private static TokenExchangeException SecretBearingFailure() =>
        new(
            TokenExchangeErrorKind.InvalidGrant,
            DelegationClient,
            $"IdP says subject {CallerToken} is invalid, issued {ExchangedToken}, client secret {IntrospectionSecret}",
            new HttpRequestException($"IdP says {CallerToken} {IntrospectionSecret}"))
        {
            StatusCode = HttpStatusCode.BadRequest,
            Error = "invalid_grant",
            ErrorDescription = $"IdP says {CallerToken} {ExchangedToken} {IntrospectionSecret}",
        };

    private static Exception FailureOf(string failure) => failure switch
    {
        "token-exchange" => new TokenExchangeException(TokenExchangeErrorKind.InvalidClient, DelegationClient, "invalid client") { StatusCode = HttpStatusCode.Unauthorized },
        "http" => new HttpRequestException("connection refused"),
        "timeout" => new TimeoutRejectedException("attempt timed out"),
        "task-canceled" => new TaskCanceledException("HttpClient.Timeout elapsed"),
        "breaker" => new BrokenCircuitException("breaker open"),
        "invalid-operation" => new InvalidOperationException("no client"),
        _ => new FormatException("unexpected"),
    };

    private HeappeTokenExchanger Exchanger(TokenExchangeOptions? options = null) =>
        new(
            _delegation,
            _cache,
            Options.Create(options ?? ConfiguredOptions()),
            _time,
            new RecordingLogger<HeappeTokenExchanger>(_logs));

    private static TokenExchangeOptions ConfiguredOptions() => new()
    {
        ClientId = IntrospectionClientId,
        ClientSecret = IntrospectionSecret,
        Authority = new Uri(TokenExchangeValues.Authority),
    };

    private static HeappeRoute Route(HeappeIdentifier? identifier = null, string? clientId = HeappeClientId, IReadOnlyList<string>? scopes = null)
    {
        var id = identifier ?? HeappeMock;
        return new HeappeRoute(id, new Uri($"http://nexus.test/api/heappe/{id}/"), clientId, scopes ?? ["openid", "heappe"]);
    }
}
