using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using UserOrganizationManagement.API.Authentication;
using UserOrganizationManagement.Domain;
using UserOrganizationManagement.Domain.Configuration;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// The legacy <c>MailSender</c> client sends the configured Emailsender API key as <c>X-Api-Key</c>,
/// which emailsender 2.4.0 requires on every request.
/// </summary>
public sealed class AuthenticationExtensionsEmailSenderClientTests
{
    private const string ApiKeyHeader = "X-Api-Key";

    [Fact]
    public async Task Should_add_api_key_header_to_mail_sender_client_When_key_configured()
    {
        await using var app = BuildApp("k-123");
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.MailSenderHttpClientName);

        Assert.True(
            client.DefaultRequestHeaders.TryGetValues(ApiKeyHeader, out var values),
            $"the {Constants.MailSenderHttpClientName} client carries no {ApiKeyHeader} header; emailsender 2.4.0 answers 401");
        Assert.Equal(["k-123"], values);
    }

    [Fact]
    public async Task Should_add_no_api_key_header_to_mail_sender_client_When_key_blank()
    {
        await using var app = BuildApp("");
        using var client = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.MailSenderHttpClientName);

        Assert.False(
            client.DefaultRequestHeaders.Contains(ApiKeyHeader),
            $"the {Constants.MailSenderHttpClientName} client sends {ApiKeyHeader} for a blank key");
    }

    private static WebApplication BuildApp(string apiKey)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MailConfiguration:MailSenderUrl"] = "http://mail.local/",
            ["MailConfiguration:MailSenderApiKey"] = apiKey
        });
        builder.Services.AddOptions<MailConfiguration>().BindConfiguration(MailConfiguration.ConfigurationPath);
        builder.AddEmailSenderClient();

        return builder.Build();
    }
}
