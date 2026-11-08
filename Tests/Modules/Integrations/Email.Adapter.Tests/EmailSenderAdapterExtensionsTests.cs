using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Userorg.Adapter.Email;

namespace Email.Adapter.Tests;

/// <summary>
/// The adapter's mail client sends the configured Emailsender API key as <c>X-Api-Key</c>, which
/// emailsender 2.4.0 requires on every request.
/// </summary>
public sealed class EmailSenderAdapterExtensionsTests
{
    private const string ApiKeyHeader = "X-Api-Key";

    [Fact]
    public void Should_send_api_key_header_When_key_configured()
    {
        using var host = BuildHost("k-123");
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpEmailSender.HttpClientName);

        Assert.True(
            client.DefaultRequestHeaders.TryGetValues(ApiKeyHeader, out var values),
            $"the {HttpEmailSender.HttpClientName} client carries no {ApiKeyHeader} header; emailsender 2.4.0 answers 401");
        Assert.Equal(["k-123"], values);
    }

    [Fact]
    public void Should_send_no_api_key_header_When_key_blank()
    {
        using var host = BuildHost("   ");
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpEmailSender.HttpClientName);

        Assert.False(
            client.DefaultRequestHeaders.Contains(ApiKeyHeader),
            $"the {HttpEmailSender.HttpClientName} client sends {ApiKeyHeader} for a blank key");
    }

    private static IHost BuildHost(string apiKey)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MailConfiguration:MailSenderUrl"] = "http://mail.local/",
            ["MailConfiguration:MailSenderApiKey"] = apiKey
        });
        builder.AddEmailSenderAdapter();

        return builder.Build();
    }
}
