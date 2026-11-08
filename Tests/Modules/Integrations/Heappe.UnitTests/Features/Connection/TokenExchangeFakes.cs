using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

using TokenDelegation.Interfaces;
using TokenDelegation.Models;

namespace Heappe.UnitTests.Features.Connection;

internal static class TokenExchangeValues
{
    public const string AuthHeaderFlag = "HEAppEAuthHeader";
    public const string DelegationClient = "heappe-token-exchange";
    public const string CallerToken = "caller-token-7f3a";
    public const string ExchangedToken = "exchanged-token-91c2";
    public const string IntrospectionClientId = "userorg-introspection";
    public const string IntrospectionSecret = "introspection-secret-5d8e";
    public const string HeappeClientId = "heappe-mock-client";
    public const string Authority = "https://keycloak.test/realms/lexis";
    public const string TokenEndpoint = "https://keycloak.test/realms/lexis/protocol/openid-connect/token";
}

internal sealed record DelegationCall(string ClientName, string SubjectToken, TokenExchangeRequestOptions? Options);

internal sealed class FakeTokenDelegationService : ITokenDelegationService
{
    private readonly ConcurrentQueue<DelegationCall> _calls = new();

    public Func<DelegationCall, CancellationToken, Task<TokenExchangeResult>> Respond { get; set; } =
        (_, _) => Task.FromResult(new TokenExchangeResult(TokenExchangeValues.ExchangedToken, TimeSpan.FromHours(1), "openid heappe"));

    public IReadOnlyCollection<DelegationCall> Calls => _calls;

    public void ExpiresIn(TimeSpan? expiresIn) =>
        Respond = (_, _) => Task.FromResult(new TokenExchangeResult(TokenExchangeValues.ExchangedToken, expiresIn, "openid heappe"));

    public void Throws(Exception exception) => Respond = (_, _) => Task.FromException<TokenExchangeResult>(exception);

    public Task<TokenExchangeResult> ExchangeDelegatedTokenAsync(string clientName, string subjectToken, CancellationToken cancellationToken = default) =>
        ExchangeDelegatedTokenAsync(clientName, subjectToken, null, cancellationToken);

    public Task<TokenExchangeResult> ExchangeDelegatedTokenAsync(string clientName, string subjectToken, TokenExchangeRequestOptions? options, CancellationToken cancellationToken = default)
    {
        var call = new DelegationCall(clientName, subjectToken, options);
        _calls.Enqueue(call);
        return Respond(call, cancellationToken);
    }

    public Task<TokenExchangeResult> ExchangeTokenAsync(string subjectClient, string requestedClient, string subjectToken, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The obsolete overload ignores the subject client; use ExchangeDelegatedTokenAsync.");

    public Task<TokenExchangeResult> ExchangeTokenAsync(string subjectClient, string requestedClient, string subjectToken, TokenExchangeRequestOptions? options, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The obsolete overload ignores the subject client; use ExchangeDelegatedTokenAsync.");
}

internal sealed class FakeFeatureManager(params string[] enabled) : IFeatureManager
{
    private readonly HashSet<string> _enabled = new(enabled, StringComparer.Ordinal);

    public async IAsyncEnumerable<string> GetFeatureNamesAsync()
    {
        foreach (var name in _enabled)
        {
            yield return name;
        }

        await Task.CompletedTask;
    }

    public Task<bool> IsEnabledAsync(string feature) => Task.FromResult(_enabled.Contains(feature));

    public Task<bool> IsEnabledAsync<TContext>(string feature, TContext context) => Task.FromResult(_enabled.Contains(feature));
}

internal sealed record LogRecord(string Category, LogLevel Level, string Message, Exception? Exception, IReadOnlyList<KeyValuePair<string, object?>> State, ActivityTraceId? TraceId)
{
    public string Text => string.Join(" | ", State.Select(pair => $"{pair.Key}={pair.Value}").Prepend(Message));
}

internal sealed class LogSink
{
    private readonly ConcurrentQueue<LogRecord> _records = new();

    public IReadOnlyCollection<LogRecord> Records => _records;

    public IEnumerable<LogRecord> Warnings => _records.Where(r => r.Level == LogLevel.Warning);

    public void Add(LogRecord record) => _records.Enqueue(record);
}

internal sealed class RecordingLogger<T>(LogSink sink) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        sink.Add(new LogRecord(
            typeof(T).FullName ?? typeof(T).Name,
            logLevel,
            formatter(state, exception),
            exception,
            state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
            Activity.Current?.TraceId));
}

internal sealed class RecordingLoggerProvider(LogSink sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, sink);

    public void Dispose()
    {
    }

    private sealed class CategoryLogger(string category, LogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add(new LogRecord(
                category,
                logLevel,
                formatter(state, exception),
                exception,
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [],
                Activity.Current?.TraceId));
    }
}

internal sealed class FakeTimeClock(TimeProvider time) : ISystemClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}

internal sealed class RecordingMemoryCache(TimeProvider time) : IMemoryCache
{
    private readonly MemoryCache _inner = new(new MemoryCacheOptions { Clock = new FakeTimeClock(time) });
    private readonly ConcurrentQueue<object> _keys = new();

    public IReadOnlyCollection<object> Keys => _keys;

    public ICacheEntry CreateEntry(object key)
    {
        _keys.Enqueue(key);
        return _inner.CreateEntry(key);
    }

    public void Remove(object key) => _inner.Remove(key);

    public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

    public void Dispose() => _inner.Dispose();
}

internal sealed class FakeIdentityProvider
{
    private readonly ConcurrentQueue<(Uri Uri, string? Authorization, string Body)> _requests = new();

    public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } = _ => Task.FromResult(Granted());

    public IReadOnlyCollection<(Uri Uri, string? Authorization, string Body)> Requests => _requests;

    public HttpMessageHandler Handler() => new Endpoint(this);

    public static HttpResponseMessage Granted(int expiresInSeconds = 3600) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"access_token":"{{TokenExchangeValues.ExchangedToken}}","token_type":"Bearer","expires_in":{{expiresInSeconds}},"issued_token_type":"urn:ietf:params:oauth:token-type:access_token"}""",
            Encoding.UTF8,
            "application/json")
    };

    private sealed class Endpoint(FakeIdentityProvider idp) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            idp._requests.Enqueue((request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return await idp.Respond(cancellationToken);
        }
    }
}
