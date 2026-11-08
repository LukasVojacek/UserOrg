using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Heappe.Contracts;
using Heappe.Features.Nexus;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using TokenDelegation.Interfaces;
using TokenDelegation.Models;

using UserOrg.Shared;

namespace Heappe.Features.Connection;

/// <summary>
/// Exchanges the caller's token through the TokenDelegation client <see cref="TokenExchangeOptions.DelegationClientName"/>,
/// caches the result by a hash of the caller's token, the client and the sorted scopes for at most 300 s, and logs
/// every failure as one Warning per identifier and cause per minute without any token, secret or IdP text.
/// Registered only when the token exchange flag is on at startup, so it never checks the flag itself.
/// </summary>
internal sealed partial class HeappeTokenExchanger(
    ITokenDelegationService delegation,
    IMemoryCache cache,
    IOptions<TokenExchangeOptions> options,
    TimeProvider time,
    ILogger<HeappeTokenExchanger> logger) : IHeappeTokenExchanger
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    public async ValueTask<Result<string, Error>> ExchangeAsync(HeappeRoute route, string bearerToken, CancellationToken ct)
    {
        if (Precondition(route) is { } refused)
        {
            return Result<string, Error>.Failure(refused, State.Error);
        }

        var key = new ExchangedTokenKey(Sha256(bearerToken), route.ClientId!, string.Join(' ', route.Scopes.Order(StringComparer.Ordinal)));
        return cache.TryGetValue(key, out string? cached) && cached is not null
            ? Result<string, Error>.Success(cached, State.Ok)
            : await ExchangeAndCacheAsync(route, bearerToken, key, ct);
    }

    private Error? Precondition(HeappeRoute route)
    {
        if (!IsExchangeTarget(route))
        {
            WarnOnce(route.Identifier, HeappeFailureCause.TokenExchangeUnavailable, null);
            return Errors.TokenExchangeUnavailable(route.Identifier);
        }

        var exchangeOptions = options.Value;
        if (string.IsNullOrWhiteSpace(exchangeOptions.ClientId)
            || string.IsNullOrWhiteSpace(exchangeOptions.ClientSecret)
            || exchangeOptions.TokenAuthorityEndpoint is null)
        {
            WarnOnce(route.Identifier, HeappeFailureCause.TokenExchangeFailed, null);
            return Errors.TokenExchangeFailed(route.Identifier, null);
        }

        return null;
    }

    private async ValueTask<Result<string, Error>> ExchangeAndCacheAsync(HeappeRoute route, string bearerToken, ExchangedTokenKey key, CancellationToken ct)
    {
        TokenExchangeResult exchanged;
        try
        {
            exchanged = await delegation.ExchangeDelegatedTokenAsync(
                TokenExchangeOptions.DelegationClientName,
                bearerToken,
                new TokenExchangeRequestOptions
                {
                    Audience = route.ClientId,
                    Scope = string.Join(' ', route.Scopes)
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // why: QS-05, any exchange failure must become Result.Failure(TokenExchangeFailed), never an unhandled exception
        catch (Exception ex)
        {
            WarnOnce(route.Identifier, HeappeFailureCause.TokenExchangeFailed, ex);
            return Result<string, Error>.Failure(Errors.TokenExchangeFailed(route.Identifier, ex), State.Error);
        }
#pragma warning restore CA1031

        if (exchanged.ExpiresIn is { } expiresIn && expiresIn > ExpiryMargin)
        {
            var lifetime = TimeSpan.FromTicks(Math.Min((expiresIn - ExpiryMargin).Ticks, MaxLifetime.Ticks));
            cache.Set(key, exchanged.AccessToken, time.GetUtcNow() + lifetime);
        }

        return Result<string, Error>.Success(exchanged.AccessToken, State.Ok);
    }

    private static bool IsExchangeTarget(HeappeRoute route) =>
        route.ClientId is { Length: > 0 } clientId
        && !clientId.Any(char.IsWhiteSpace)
        && route.Scopes.Count > 0
        && route.Scopes.All(scope => ScopeToken().IsMatch(scope));

    private void WarnOnce(HeappeIdentifier identifier, string cause, Exception? exception)
    {
        var key = new WarningKey(identifier.ToString(), cause);
        if (cache.TryGetValue(key, out _))
        {
            return;
        }

        cache.Set(key, true, time.GetUtcNow() + WarningInterval);
        var failure = exception as TokenExchangeException;
        LogExchangeFailure(logger, identifier.ToString(), cause, exception?.GetType().Name ?? "none", failure?.Kind.ToString() ?? "none", failure?.StatusCode);
    }

    private static string Sha256(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [GeneratedRegex(@"^[\x21\x23-\x5B\x5D-\x7E]+$")]
    private static partial Regex ScopeToken();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token exchange for HEAppE instance '{Identifier}' failed with cause '{Cause}', exception type '{ExceptionType}', kind '{Kind}', status code '{StatusCode}'.")]
    private static partial void LogExchangeFailure(ILogger logger, string identifier, string cause, string exceptionType, string kind, HttpStatusCode? statusCode);

    private sealed record ExchangedTokenKey(string TokenHash, string ClientId, string Scopes);

    private sealed record WarningKey(string Identifier, string Cause);
}