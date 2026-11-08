using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using UserOrg.ServiceDefaults;

namespace ServiceDefaults.Tests;

/// <summary>
/// Verifies that <see cref="Extensions.AddDefaultHealthChecks{TBuilder}"/> registers the
/// correct checks with the correct tags and failure statuses based on configuration.
/// No external services are started; the DI container is built but checks are never invoked.
/// </summary>
public sealed class HealthCheckRegistrationTests
{

    private static WebApplicationBuilder CreateBuilder(IEnumerable<KeyValuePair<string, string?>>? config = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = "ServiceDefaults.Tests"
        });

        if (config is not null)
        {
            builder.Configuration.AddInMemoryCollection(config);
        }

        return builder;
    }

    private static async Task<IReadOnlyCollection<HealthCheckRegistration>> GetRegistrationsAsync(
        IEnumerable<KeyValuePair<string, string?>>? config = null)
    {
        var builder = CreateBuilder(config);
        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        return (IReadOnlyCollection<HealthCheckRegistration>)app.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
    }

    [Fact(DisplayName = "Should_RegisterSelf_Always")]
    public async Task Should_RegisterSelf_Always()
    {
        var registrations = await GetRegistrationsAsync();
        Assert.Contains(registrations, r => r.Name == "self");
    }

    [Fact(DisplayName = "Should_RegisterMemory_Always")]
    public async Task Should_RegisterMemory_Always()
    {
        var registrations = await GetRegistrationsAsync();
        Assert.Contains(registrations, r => r.Name == "memory");
    }

    [Fact(DisplayName = "Should_NotRegisterDatabase_When_ConnectionStringAbsent")]
    public async Task Should_NotRegisterDatabase_When_ConnectionStringAbsent()
    {
        var registrations = await GetRegistrationsAsync();
        Assert.DoesNotContain(registrations, r => r.Name == "database");
    }

    [Fact(DisplayName = "Should_RegisterDatabase_When_ConnectionStringPresent")]
    public async Task Should_RegisterDatabase_When_ConnectionStringPresent()
    {
        var registrations = await GetRegistrationsAsync([
            new("ConnectionStrings:UserOrgDBContext", "Server=localhost;Database=test;")
        ]);
        Assert.Contains(registrations, r => r.Name == "database");
    }

    [Fact(DisplayName = "Should_NotRegisterVault_When_VaultUrlAbsent")]
    public async Task Should_NotRegisterVault_When_VaultUrlAbsent()
    {
        var registrations = await GetRegistrationsAsync();
        Assert.DoesNotContain(registrations, r => r.Name == "vault");
    }

    [Fact(DisplayName = "Should_RegisterVault_When_VaultUrlPresent")]
    public async Task Should_RegisterVault_When_VaultUrlPresent()
    {
        var registrations = await GetRegistrationsAsync([
            new("Services:vault:http", "http://localhost:8200")
        ]);
        Assert.Contains(registrations, r => r.Name == "vault");
    }

    [Fact(DisplayName = "Should_NotRegisterEmailSender_When_MailUrlAbsent")]
    public async Task Should_NotRegisterEmailSender_When_MailUrlAbsent()
    {
        var registrations = await GetRegistrationsAsync();
        Assert.DoesNotContain(registrations, r => r.Name == "emailsender");
    }

    [Fact(DisplayName = "Should_RegisterEmailSender_When_MailUrlPresent")]
    public async Task Should_RegisterEmailSender_When_MailUrlPresent()
    {
        var registrations = await GetRegistrationsAsync([
            new("MailConfiguration:MailSenderUrl", "http://localhost:8300")
        ]);
        Assert.Contains(registrations, r => r.Name == "emailsender");
    }



    [Theory(DisplayName = "Should_SetUnhealthy_FailureStatus_For_CriticalChecks")]
    [InlineData("database", "ConnectionStrings:UserOrgDBContext", "Server=test;")]
    [InlineData("vault", "Services:vault:http", "http://localhost:8200")]
    public async Task Should_SetUnhealthy_FailureStatus_For_CriticalChecks(
        string checkName, string configKey, string configValue)
    {
        var registrations = await GetRegistrationsAsync([new(configKey, configValue)]);
        var registration = registrations.Single(r => r.Name == checkName);

        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
    }

    [Theory(DisplayName = "Should_SetDegraded_FailureStatus_For_NonCriticalChecks")]
    [InlineData("emailsender", "MailConfiguration:MailSenderUrl", "http://localhost:8300")]
    [InlineData("memory", null, null)]
    public async Task Should_SetDegraded_FailureStatus_For_NonCriticalChecks(
        string checkName, string? configKey, string? configValue)
    {
        IEnumerable<KeyValuePair<string, string?>>? config = configKey is not null
            ? [new(configKey, configValue)]
            : null;

        var registrations = await GetRegistrationsAsync(config);
        var registration = registrations.Single(r => r.Name == checkName);

        Assert.Equal(HealthStatus.Degraded, registration.FailureStatus);
    }

    [Fact(DisplayName = "Should_TagSelf_With_Live")]
    public async Task Should_TagSelf_With_Live()
    {
        var registrations = await GetRegistrationsAsync();
        var self = registrations.Single(r => r.Name == "self");
        Assert.Contains("live", self.Tags);
    }

    [Fact(DisplayName = "Should_Only_SelfCheck_Have_LiveTag_When_AllChecksRegistered")]
    public async Task Should_Only_SelfCheck_Have_LiveTag_When_AllChecksRegistered()
    {
        var registrations = await GetRegistrationsAsync([
            new("ConnectionStrings:UserOrgDBContext", "Server=test;"),
            new("Services:vault:http", "http://localhost:8200"),
            new("MailConfiguration:MailSenderUrl", "http://localhost:8300"),
            new("Services:immugw:http", "http://localhost:3323")
        ]);

        var liveChecks = registrations.Where(r => r.Tags.Contains("live")).ToList();
        Assert.Single(liveChecks);
        Assert.Equal("self", liveChecks[0].Name);
    }

    [Fact(DisplayName = "Should_SelfCheck_ReturnHealthy_Always")]
    public async Task Should_SelfCheck_ReturnHealthy_Always()
    {
        var builder = CreateBuilder();
        builder.AddDefaultHealthChecks();
        await using var app = builder.Build();

        var service = app.Services.GetRequiredService<HealthCheckService>();
        var report = await service.CheckHealthAsync(r => r.Name == "self");

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Equal(HealthStatus.Healthy, report.Entries["self"].Status);
    }
}
