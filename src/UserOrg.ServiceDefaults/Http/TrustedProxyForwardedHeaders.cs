using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using IPNetwork = System.Net.IPNetwork;

namespace UserOrg.ServiceDefaults.Http;

/// <summary>The proxies whose X-Forwarded-For and X-Forwarded-Proto a host applies; both lists empty means loopback only.</summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    public IReadOnlyList<string> KnownNetworks { get; set; } = [];

    public IReadOnlyList<string> KnownProxies { get; set; } = [];

    internal bool IsConfigured => KnownNetworks.Count > 0 || KnownProxies.Count > 0;
}

public static partial class TrustedProxyForwardedHeaders
{
    private const int WidestQuietIPv4Prefix = 16;
    private const int WidestQuietIPv6Prefix = 48;
    private const int IPv4MappedPrefix = 96;

    private static readonly UInt128 IPv4MappedRange = (UInt128)0xFFFF << 32;

    public static IServiceCollection AddTrustedProxyForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection(TrustedProxyOptions.SectionName))
            .Validate(
                options => options.KnownProxies.All(proxy => IPAddress.TryParse(proxy, out _)),
                $"{TrustedProxyOptions.SectionName}:KnownProxies holds an entry that is not an IP address.")
            .Validate(
                options => options.KnownNetworks.All(network => IPNetwork.TryParse(network, out _)),
                $"{TrustedProxyOptions.SectionName}:KnownNetworks holds an entry that is not a CIDR network.")
            .Validate(
                options => !options.KnownNetworks.Any(TrustsEveryIPv4Client),
                $"{TrustedProxyOptions.SectionName}:KnownNetworks must not trust every IPv4 client (0.0.0.0/0, ::/0 or a network covering ::ffff:0:0/96).")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<TrustedProxyOptions>>((options, trusted) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1;

                var configured = trusted.Value;
                if (!configured.IsConfigured)
                {
                    return;
                }

                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in configured.KnownProxies)
                {
                    options.KnownProxies.Add(IPAddress.Parse(proxy));
                }

                foreach (var network in configured.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }

    public static IApplicationBuilder UseTrustedProxyForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ApplicationServices;
        var trusted = services.GetRequiredService<IOptions<TrustedProxyOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TrustedProxyForwardedHeaders));

        if (!trusted.IsConfigured && !services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            LogSectionMissing(logger, TrustedProxyOptions.SectionName);
        }

        foreach (var network in trusted.KnownNetworks)
        {
            if (WidestQuietPrefix(network) is { } prefix)
            {
                LogWideNetwork(logger, network, prefix);
            }
        }

        return app.UseForwardedHeaders();
    }

    private static bool TrustsEveryIPv4Client(string network) =>
        IPNetwork.TryParse(network, out var parsed)
        && (parsed.PrefixLength == 0 || CoversIPv4MappedRange(parsed));

    private static bool CoversIPv4MappedRange(IPNetwork network)
    {
        if (network.BaseAddress.AddressFamily != AddressFamily.InterNetworkV6 || network.PrefixLength > IPv4MappedPrefix)
        {
            return false;
        }

        var mask = UInt128.MaxValue << (128 - network.PrefixLength);
        return (IPv4MappedRange & mask) == BinaryPrimitives.ReadUInt128BigEndian(network.BaseAddress.GetAddressBytes());
    }

    private static int? WidestQuietPrefix(string network)
    {
        var parsed = IPNetwork.Parse(network);
        var widest = parsed.BaseAddress.AddressFamily == AddressFamily.InterNetwork
            ? WidestQuietIPv4Prefix
            : WidestQuietIPv6Prefix;
        return parsed.PrefixLength < widest ? widest : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No {Section} section is configured, so only loopback is a trusted proxy; set KnownProxies or KnownNetworks to the proxy in front of this host")]
    private static partial void LogSectionMissing(ILogger logger, string section);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Trusted proxy network {Network} is wider than /{Prefix}; every address in it can set the client IP")]
    private static partial void LogWideNetwork(ILogger logger, string network, int prefix);
}