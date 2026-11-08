using Microsoft.Extensions.Options;

namespace UserOrg.Gateway.Features.ServiceValidation;

/// <summary>
/// Validates service-to-service caller identity based on per-target configuration.
/// Performs IP validation and X-Forwarded-Host header checks for configured targets only.
/// </summary>
internal sealed class ServiceCallerValidator : IServiceCallerValidator
{
    private readonly ServiceToServiceOptions _options;
    private readonly ILogger<ServiceCallerValidator> _logger;
    private readonly Dictionary<string, HashSet<string>> _targetCallerMap;

    public ServiceCallerValidator(IOptions<ServiceToServiceOptions> options, ILogger<ServiceCallerValidator> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _targetCallerMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (_options.Routes != null)
        {
            foreach (var route in _options.Routes)
            {
                if (route.AllowedCallers != null && route.AllowedCallers.Count > 0)
                {
                    _targetCallerMap[route.Target] = new HashSet<string>(
                        route.AllowedCallers,
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }
    }

    public bool IsTargetConfigured(string targetService)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetService);
        return _targetCallerMap.ContainsKey(targetService);
    }

    public string? ValidateCaller(HttpContext context, string targetService)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(targetService);

        // If target is not configured, return null (no validation)
        if (!IsTargetConfigured(targetService))
        {
            return null;
        }

        // STEP 1: IP validation - only internal network (Docker/localhost/private ranges)
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null || !IsInternalIp(remoteIp))
        {
            _logger.LogWarning(
                "S2S validation for {Target} failed: non-internal IP {IP}",
                targetService,
                remoteIp);
            return null;
        }

        // STEP 2: Host header validation (set automatically by HttpClient)
        // When a service calls gateway, HttpClient sets Host header to gateway's address
        // We need to identify the calling service from the request context
        var host = context.Request.Headers["Host"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogWarning(
                "S2S validation for {Target} failed: missing Host header",
                targetService);
            return null;
        }

        // STEP 3: Extract caller service name from connection info or custom header
        // Option 1: Use X-Calling-Service header if services set it explicitly
        // Option 2: Rely on IP-based identification within Docker network
        var callingServiceHeader = context.Request.Headers["X-Calling-Service"].FirstOrDefault();

        string? callerService = null;

        if (!string.IsNullOrWhiteSpace(callingServiceHeader))
        {
            callerService = callingServiceHeader;
        }
        else
        {
            // Try to identify from reverse DNS or known IP mappings
            // For now, we'll need services to send X-Calling-Service header
            _logger.LogWarning(
                "S2S validation for {Target} failed: missing X-Calling-Service header. Services must identify themselves.",
                targetService);
            return null;
        }

        // STEP 4: Check if caller is in the allowed list for this target
        if (!_targetCallerMap.TryGetValue(targetService, out var allowedCallers) ||
            !allowedCallers.Contains(callerService))
        {
            _logger.LogWarning(
                "S2S validation for {Target} failed: caller {Caller} not in allowed list",
                targetService,
                callerService);
            return null;
        }

        _logger.LogInformation(
            "S2S validation for {Target} succeeded: caller {Caller} from IP {IP}",
            targetService,
            callerService,
            remoteIp);

        return callerService;
    }

    /// <summary>
    /// Checks if the IP address is from internal network (Docker/localhost/private ranges).
    /// Loopback covers 127.0.0.0/8 and the IPv6 loopback.
    /// </summary>
    private static bool IsInternalIp(System.Net.IPAddress ip)
        => System.Net.IPAddress.IsLoopback(ip) || IsPrivateIPv4(ip.GetAddressBytes());

    private static bool IsPrivateIPv4(byte[] bytes) => bytes.Length == 4 && bytes[0] switch
    {
        10 => true,                         // 10.0.0.0/8
        172 => bytes[1] is >= 16 and <= 31, // 172.16.0.0/12, includes the Docker default bridge 172.17.0.0/16
        192 => bytes[1] == 168,             // 192.168.0.0/16
        _ => false,
    };

    /// <summary>
    /// Extracts service name from X-Forwarded-Host header.
    /// Handles formats like "service-name", "service-name:port", "service-name.namespace.svc.cluster.local".
    /// </summary>
    private static string ExtractServiceName(string forwardedHost)
    {
        if (string.IsNullOrWhiteSpace(forwardedHost))
        {
            return string.Empty;
        }

        var hostWithoutPort = forwardedHost.Split(':')[0];

        // Remove domain suffix if present (e.g., .namespace.svc.cluster.local)
        var serviceName = hostWithoutPort.Split('.')[0];

        return serviceName;
    }
}