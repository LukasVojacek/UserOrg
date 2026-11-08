using Microsoft.Extensions.Logging;

using UserOrg.Shared.Http;

using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace UserOrg.Gateway.Startup;

internal static partial class PermissionsChangedTransformExtensions
{
    internal static TransformBuilderContext AddPermissionsChangedTransform(this TransformBuilderContext context)
    {
        context.AddRequestHeader(PermissionsChangedHeader.CapabilityHeader, PermissionsChangedHeader.Capability, append: false);

        context.AddResponseTransform(async transformContext =>
        {
            var value = ResponseTransform.TakeHeader(transformContext, PermissionsChangedHeader.Name);
            if (value.Count == 0)
                return;

            var services = transformContext.HttpContext.RequestServices;
            var logger = services.GetRequiredService<ILogger<Program>>();
            var ids = PermissionsChangedHeader.Parse(value);
            if (ids.Count == 0)
            {
                logger.LogMalformedPermissionsChangedHeader();
                return;
            }

            try
            {
                var cache = services.GetRequiredService<IUserPermissionsCache>();
                if (ids is [PermissionsChangedHeader.EvictAll])
                {
                    await cache.InvalidateAllAsync(transformContext.HttpContext.RequestAborted);
                    return;
                }

                foreach (var id in ids)
                    await cache.InvalidateAsync(id, transformContext.HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                logger.LogPermissionSnapshotEvictionFailed(ex, string.Join(',', ids));
            }
        });

        return context;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed permissions-changed header, no snapshot evicted")]
    private static partial void LogMalformedPermissionsChangedHeader(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Evicting permission snapshots for {IdpUserIds} failed")]
    private static partial void LogPermissionSnapshotEvictionFailed(this ILogger logger, Exception exception, string idpUserIds);
}