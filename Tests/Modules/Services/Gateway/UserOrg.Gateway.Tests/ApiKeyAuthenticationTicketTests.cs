using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using HashiCorpVault.Contracts;
using HashiCorpVault.Contracts.Models;

using Lukdrasil.Error;
using Lukdrasil.Result;
using Lukdrasil.StepUpLogging;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using UserOrg.Gateway.Configuration;
using UserOrg.Shared;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes the principal <c>ApiKeyAuthenticationHandler</c> builds for each kind of vault entry, so the
/// claim set survives the handler being split up.
/// </summary>
public sealed class ApiKeyAuthenticationTicketTests
{
    private const string Key = "ticket-key-value-5150";

    private const string ServiceName = "svc-ticket";

    private const string ContactEmail = "ops@example.org";

    private static async Task<ClaimsPrincipal> AuthenticateAsync(ServiceAccessKeys vaultEntry, string headerName, string headerValue, CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace);
            }
        });
        services.AddHttpContextAccessor();
        services.AddSingleton<RecordingAuditEventSink>();
        services.AddSingleton(typeof(IAuditLogger<>), typeof(RecordingAuditLogger<>));
        services.AddDistributedMemoryCache();
        services.AddHybridCache();
        services.Configure<ApiKeyAuthenticationOptions>(options => options.TimeoutSeconds = 5);

        IReadOnlyCollection<ServiceAccessKeys> keys = [vaultEntry];
        var vault = new Mock<IHashiCorpVaultClient>();
        vault.Setup(v => v.GetServiceAccessKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyCollection<ServiceAccessKeys>, Error>.Success(keys));
        services.AddSingleton(vault.Object);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Constants.ApiKeySchemeId, _ => { });

        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Headers[headerName] = headerValue;

        var result = await httpContext.AuthenticateAsync(Constants.ApiKeySchemeId);

        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.Principal!;
    }

    private static ServiceAccessKeys Entry(EServiceType serviceType, string? data = null, string contactEmail = "")
        => new() { Name = ServiceName, Key = Key, BackupKey = "unused-backup", ServiceType = serviceType, Data = data, ContactEmail = contactEmail };

    private static string?[] Values(ClaimsPrincipal principal, string claimType)
        => [.. principal.FindAll(claimType).Select(claim => claim.Value)];

    private static ActivityListener ListenToApiKeyActivities(List<Activity> recorded)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "UserOrg.Gateway.ApiKeyAuth",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Should_LogTheServiceNameButNotTheContactEmail_When_AServiceKeyWithAContactEmailAuthenticates()
    {
        using var logs = new CapturingLoggerProvider();

        await AuthenticateAsync(Entry(EServiceType.Service, "custom-7", ContactEmail), "Authorization", $"Service {Key}", logs);

        Assert.Contains(logs.All, entry => entry.Message.Contains(ServiceName, StringComparison.Ordinal));
        var leaks = logs.All.Where(entry => entry.Message.Contains(ContactEmail, StringComparison.OrdinalIgnoreCase)).Select(entry => $"[{entry.Level}] {entry.Message}").ToList();
        Assert.True(leaks.Count == 0, $"the contact email reached the log: {string.Join(" | ", leaks)}");
    }

    [Fact]
    public async Task Should_TagTheServiceNameButNotTheContactEmail_When_AServiceKeyWithAContactEmailAuthenticates()
    {
        List<Activity> recorded = [];
        using var listener = ListenToApiKeyActivities(recorded);

        await AuthenticateAsync(Entry(EServiceType.Service, "custom-7", ContactEmail), "Authorization", $"Service {Key}");

        Assert.Single(recorded, activity => (string?)activity.GetTagItem("auth.service_name") == ServiceName);
        Assert.DoesNotContain(recorded.SelectMany(activity => activity.TagObjects), tag => tag.Value?.ToString()?.Contains(ContactEmail, StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Should_IssueAServicePrincipalWithEmailAndCustomData_When_AServiceKeyCarriesBoth()
    {
        var principal = await AuthenticateAsync(Entry(EServiceType.Service, "custom-7", ContactEmail), "Authorization", $"Service {Key}");

        Assert.Equal([ServiceName], Values(principal, JwtRegisteredClaimNames.Sub));
        Assert.Equal([ServiceName], Values(principal, JwtRegisteredClaimNames.PreferredUsername));
        Assert.Equal([nameof(EApiKeyType.Service)], Values(principal, ClaimTypes.Role));
        Assert.Equal([nameof(EAuthorizationMethod.ApiKey)], Values(principal, EAuthorizationMethod.ClaimType));
        Assert.Equal([nameof(EServiceType.Service)], Values(principal, AuthConstants.ServiceTypeClaimType));
        Assert.Equal([ContactEmail], Values(principal, JwtRegisteredClaimNames.Email));
        Assert.Equal(["service"], Values(principal, AuthConstants.IdentityTypeClaimType));
        Assert.Equal(["custom-7"], Values(principal, AuthConstants.CustomDataTypeClaimType));
        Assert.Empty(Values(principal, AuthConstants.ProviderIdClaimType));
    }

    [Fact]
    public async Task Should_IssueAProviderPrincipalKeyedByTheProviderId_When_AProviderKeyAuthenticates()
    {
        var principal = await AuthenticateAsync(Entry(EServiceType.Provider, "provider-42"), "X-Api-ProviderServiceKey", Key);

        Assert.Equal(["provider-42"], Values(principal, JwtRegisteredClaimNames.Sub));
        Assert.Equal([ServiceName], Values(principal, JwtRegisteredClaimNames.PreferredUsername));
        Assert.Equal([nameof(EApiKeyType.LegacyProvider)], Values(principal, ClaimTypes.Role));
        Assert.Equal([nameof(EServiceType.Provider)], Values(principal, AuthConstants.ServiceTypeClaimType));
        Assert.Equal(["provider"], Values(principal, AuthConstants.IdentityTypeClaimType));
        Assert.Equal(["provider-42"], Values(principal, AuthConstants.ProviderIdClaimType));
        Assert.Empty(Values(principal, JwtRegisteredClaimNames.Email));
        Assert.Empty(Values(principal, AuthConstants.CustomDataTypeClaimType));
    }

    [Fact]
    public async Task Should_IssueAServicePrincipalWithoutCustomData_When_ADdiKeyAuthenticates()
    {
        var principal = await AuthenticateAsync(Entry(EServiceType.DDI, "ignored-data"), "X-Api-Token", Key);

        Assert.Equal([ServiceName], Values(principal, JwtRegisteredClaimNames.Sub));
        Assert.Equal([nameof(EApiKeyType.LegacyService)], Values(principal, ClaimTypes.Role));
        Assert.Equal([nameof(EServiceType.DDI)], Values(principal, AuthConstants.ServiceTypeClaimType));
        Assert.Equal(["service"], Values(principal, AuthConstants.IdentityTypeClaimType));
        Assert.Empty(Values(principal, AuthConstants.CustomDataTypeClaimType));
        Assert.Empty(Values(principal, AuthConstants.ProviderIdClaimType));
    }
}
