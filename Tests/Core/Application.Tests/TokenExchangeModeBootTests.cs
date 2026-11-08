using System.Net;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// Boots the real <c>Program</c> in each HEAppE routing mode, decided once at startup by
/// <c>FeatureManagement:TokenExchangeEnabled</c>. QS-08: with the flag absent and only
/// <c>TokenExchange__Nexus__BaseUrl</c> set (no <c>TokenExchange__Nexus__ClientId</c> or
/// <c>TokenExchange__Nexus__ClientSecret</c>) the API starts. QS-10: with the flag on and the NexusApi service account
/// missing, startup fails naming the key.
/// </summary>
[Collection(ProgramHostBootTestCollection.Name)]
public sealed class TokenExchangeModeBootTests
{
    [Fact]
    public async Task Qs08_with_the_flag_absent_and_only_the_nexus_base_url_set_the_api_starts()
    {
        await using var factory = new TokenExchangeModeFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = factory.Services.GetRequiredService<IConfiguration>();
        Assert.Null(configuration["FeatureManagement:TokenExchangeEnabled"]);
        Assert.Null(configuration["TokenExchange:Nexus:ClientId"]);
        Assert.Null(configuration["TokenExchange:Nexus:ClientSecret"]);
        Assert.Equal(ProgramBootEnvironment.DefaultNexusBaseUrl, configuration["TokenExchange:Nexus:BaseUrl"]);
    }

    [Fact]
    public void Qs08_with_the_flag_absent_no_token_delegation_and_no_registry_client_are_registered()
    {
        using var factory = new TokenExchangeModeFactory();
        using var client = factory.CreateClient();

        Assert.DoesNotContain(factory.Registrations, d => d.ServiceType.FullName == "TokenDelegation.Interfaces.ITokenDelegationService");
        Assert.DoesNotContain(factory.Registrations, d => d.ServiceType.FullName == "Heappe.Features.Nexus.NexusRegistryClient");
        var exchanger = Assert.Single(factory.Registrations, d => d.ServiceType.FullName == "Heappe.Features.Connection.IHeappeTokenExchanger");
        using var scope = factory.Services.CreateScope();
        Assert.Equal("PassThroughTokenExchanger", scope.ServiceProvider.GetRequiredService(exchanger.ServiceType).GetType().Name);
    }

    [Fact]
    public async Task Qs10_with_the_flag_on_and_no_nexus_service_account_the_api_fails_to_start_naming_the_key()
    {
        await using var factory = new TokenExchangeModeFactory(
            ("FeatureManagement:TokenExchangeEnabled", "true"),
            ("TokenExchange:ClientId", "userorg"),
            ("TokenExchange:ClientSecret", "secret"),
            ("TokenExchange:TokenAuthorityEndpoint", "https://idp.example.test/token"),
            ("TokenExchange:Nexus:BaseUrl", "https://nexus.example.test/"));

        var failure = Record.Exception(() => factory.CreateClient().Dispose());

        Assert.NotNull(failure);
        var chain = failure.ToString();
        Assert.True(
            chain.Contains("TokenExchange:Nexus: 'ClientId'", StringComparison.Ordinal)
            || chain.Contains("TokenExchange:Nexus: 'ClientSecret'", StringComparison.Ordinal),
            "The startup failure must name the ClientId or ClientSecret of the TokenExchange:Nexus section. Actual: " + chain[..Math.Min(chain.Length, 1500)]);
    }

    /// <summary>Boots <c>Program</c> offline with <paramref name="settings"/> layered on top of <see cref="ProgramBootEnvironment"/>.</summary>
    private sealed class TokenExchangeModeFactory(params (string Key, string Value)[] settings) : WebApplicationFactory<Program>
    {
        static TokenExchangeModeFactory()
        {
            ProgramBootEnvironment.Apply();
        }

        public IReadOnlyList<ServiceDescriptor> Registrations { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                ProgramBootEnvironment.RemoveHostedServices(services);
                Registrations = [.. services];
            });
        }
    }
}
