using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using UserOrg.Shared.Email;
using UserOrg.Shared.Http;

namespace Userorg.Adapter.Email;

internal static class EmailSenderAdapterExtensions
{
    internal static IHostApplicationBuilder AddEmailSenderAdapter(this IHostApplicationBuilder hostBuilder)
    {
        ArgumentNullException.ThrowIfNull(hostBuilder);

        hostBuilder.Services.AddScoped<IEmailSender, HttpEmailSender>();

        hostBuilder.Services.AddOptions<EmailSenderOptions>()
            .BindConfiguration(EmailSenderOptions.ConfigurationPath)
            // Absent/whitespace is legitimate (build-time OpenAPI generation calls AddModules with a
            // placeholder configuration) and yields no BaseAddress below; a present-but-unparseable value is a
            // config typo and must fail startup, not surface as a silently-swallowed send failure later.
            .Validate(
                o => string.IsNullOrWhiteSpace(o.MailSenderUrl) || Uri.TryCreate(o.MailSenderUrl, UriKind.Absolute, out _),
                $"{EmailSenderOptions.ConfigurationPath}:{nameof(EmailSenderOptions.MailSenderUrl)} must be an absolute URI when set.")
            .ValidateOnStart();

        hostBuilder.Services.AddHttpClient(HttpEmailSender.HttpClientName, (serviceProvider, client) =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "UserOrg");
            client.Timeout = TimeSpan.FromSeconds(30);

            var options = serviceProvider.GetRequiredService<IOptions<EmailSenderOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.MailSenderUrl))
            {
                client.BaseAddress = new Uri(options.MailSenderUrl);
            }

            EmailSenderApiKey.ApplyTo(client, options.MailSenderApiKey);
        });

        return hostBuilder;
    }
}