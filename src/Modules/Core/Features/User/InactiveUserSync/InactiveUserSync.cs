using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Core.Features.User.InactiveUserSync;

/// <summary>
/// Weekly background sync: fetches list of invalid/inactive users from DDI/DRE
/// and marks them as deleted (IsDeleted = true) in the database.
///
/// Follows the same pattern as ResourceRequestExpirySweeper (polling with PeriodicTimer).
/// </summary>
internal sealed class InactiveUserSync(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<InactiveUserSyncOptions> options,
    TimeProvider timeProvider,
    ILogger<InactiveUserSync> logger) : BackgroundService
{
    // Deliberate: sync interval is the operational knob; poll cadence is derived from it
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Inactive user sync started. First run in {StartupDelay}, then every {SyncInterval}",
            StartupDelay,
            options.Value.SyncInterval);

        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.SyncInterval, timeProvider);
        do
        {
            await SyncOnceAsync(stoppingToken);
        }
        while (await WaitForNextAsync(timer, stoppingToken));
    }

    internal async Task SyncOnceAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Starting inactive user sync from {Url}", options.Value.GetDisabledUsersUri());

            var invalidUserIds = await FetchInvalidUserIdsAsync(ct);

            if (invalidUserIds.Count == 0)
            {
                logger.LogInformation("No inactive users to process");
                return;
            }

            logger.LogInformation("Fetched {Count} inactive user(s)", invalidUserIds.Count);

            using var scope = scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IRepository<Domain.User.User, IdpId>>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await MarkAsDeletedAsync(users, unitOfWork, invalidUserIds, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogInformation("Inactive user sync cancelled during shutdown");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error fetching inactive users from {Url}", options.Value.GetDisabledUsersUri());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Inactive user sync failed");
        }
    }

    internal async Task<List<Guid>> FetchInvalidUserIdsAsync(CancellationToken ct)
    {
        var httpClient = httpClientFactory.CreateClient();

        var accessToken = await GetAccessTokenAsync(httpClient, ct);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException("Failed to obtain access token for inactive user sync");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, options.Value.GetDisabledUsersUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Failed to fetch inactive users: {response.StatusCode}",
                null,
                response.StatusCode);
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        return ParseInvalidUserIds(content);
    }

    private async Task<string> GetAccessTokenAsync(HttpClient httpClient, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.GetTokenUri());

        var body = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", options.Value.ClientId },
            { "client_secret", options.Value.ClientSecret }
        };

        request.Content = new FormUrlEncodedContent(body);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to obtain access token from {TokenUrl}: {StatusCode}",
                    options.Value.GetTokenUri(),
                    response.StatusCode);
                return string.Empty;
            }

            // Response format: { "access_token": "...", "expires_in": 300, "token_type": "Bearer" }
            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            return tokenResponse?.AccessToken ?? string.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error obtaining access token from {TokenUrl}", options.Value.GetTokenUri());
            return string.Empty;
        }
    }

    internal static List<Guid> ParseInvalidUserIds(string text)
    {
        var result = new List<Guid>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        // Parse format: user_id="<guid>"
        // Each line contains one user_id="<guid>" entry
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            const string prefix = "user_id=\"";
            const string suffix = "\"";

            if (trimmedLine.StartsWith(prefix) && trimmedLine.EndsWith(suffix))
            {
                var guidStr = trimmedLine
                    .Substring(prefix.Length, trimmedLine.Length - prefix.Length - suffix.Length)
                    .Trim();

                if (Guid.TryParse(guidStr, out var guid))
                {
                    result.Add(guid);
                }
            }
        }

        return result;
    }

    private async Task MarkAsDeletedAsync(IRepository<Domain.User.User, IdpId> users, IUnitOfWork unitOfWork, List<Guid> userIds, CancellationToken ct)
    {
        var markedCount = 0;
        var skippedCount = 0;

        foreach (var userId in userIds)
        {
            try
            {
                var user = await users.FindAsync(new IdpId(userId.ToString()), ct);
                if (user == null)
                {
                    logger.LogDebug("User {UserId} not found in database", userId);
                    skippedCount++;
                    continue;
                }

                if (user.IsDeleted)
                {
                    logger.LogDebug("User {UserId} already marked as deleted", userId);
                    skippedCount++;
                    continue;
                }

                user.Deactivate();
                markedCount++;

                logger.LogDebug("Marked user {UserId} as deleted", userId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to mark user {UserId} as deleted", userId);
            }
        }
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Inactive user sync completed: {MarkedCount} marked as deleted, {SkippedCount} skipped",
            markedCount,
            skippedCount);
    }

    private static async Task<bool> WaitForNextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}