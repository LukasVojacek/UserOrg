using System;
using System.Net;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace WebsocketStompClient;

public static class ServiceCollectionExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public void AddWaldurStompClient(Action<StompClientOptions>? configure = null)
            => builder.AddWaldurStompClient(StompClientOptions.SectionName, configure);

        public void AddWaldurStompClient(string configurationSection)
            => builder.AddWaldurStompClient(configurationSection, null);

        private void AddWaldurStompClient(string configurationSection, Action<StompClientOptions>? configure = null)
        {
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }
            else
            {
                builder.Services.AddOptionsWithValidateOnStart<StompClientOptions>().Bind(builder.Configuration.GetSection(configurationSection));

            }

            builder.Services.AddSingleton<StompClientOptions>(sp => sp.GetRequiredService<IOptions<StompClientOptions>>().Value);

            builder.Services.AddSingleton<IStompClient>(sp =>
            {
                var options = sp.GetRequiredService<StompClientOptions>();
                IWebProxy? proxy = options.ProxyUrl is { Length: > 0 } proxyUrl
                    ? new WebProxy(proxyUrl)
                    : null;
                return new StompClient(
                    options.Url,
                    options.ReconnectEnable,
                    options.StompVersion,
                    options.ReconnectTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(options.ReconnectTimeoutMs.Value) : null,
                    options.HeartBeat,
                    proxy);
            });
        }
    }
}