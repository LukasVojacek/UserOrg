using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Forge.Core.Features.EndpointMap.Persistence;
using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

using UserOrg.Shared;

namespace Forge.Core.Features.EndpointMap.Services;

internal sealed class EndpointPermissionService(
    IDbContextFactory<ForgeDbContext> dbContextFactory,
    HybridCache cache,
    ILogger<EndpointPermissionService> logger) : IEndpointPermissionService
{
    private const string SnapshotCacheKey = "EndpointPermissionService:Snapshot";

    private sealed record MappingEntry(
        string EndpointName,
        string PathTemplate,
        string MatcherPattern,
        string? HttpMethod,
        IList<string> Permissions,
        int Specificity)
    {
        [field: JsonIgnore]
        [JsonIgnore]
        public Regex Matcher => field ??= new Regex(
            MatcherPattern,
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    };

    public async Task<Result<IList<string>, Error>> GetEndpointPermissionsAsync(string endpointName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return Result<IList<string>, Error>.Failure(EndpointPermissionErrors.InvalidEndpointName);
        }

        using var context = dbContextFactory.CreateDbContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var projected = await context.EndpointMappings
            .AsNoTracking()
            .Where(e => e.EndpointName == endpointName)
            .Select(e => new { Permissions = e.Permissions.Select(p => p.Key.Value) })
            .FirstOrDefaultAsync(cancellationToken);

        if (projected is null)
        {
            return Result<IList<string>, Error>.Failure(EndpointPermissionErrors.EndpointNotFound(endpointName));
        }

        return Result<IList<string>, Error>.Success(projected.Permissions.ToList());
    }
    public async Task<Result<EndpointPermissionMatch?, Error>> GetEndpointPermissionsByPathAsync(string path, string httpMethod, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<EndpointPermissionMatch?, Error>.Failure(EndpointPermissionErrors.EndpointNotFound(path ?? string.Empty));
        }

        // Get snapshot from cache with stampede protection
        var snapshot = await cache.GetOrCreateAsync(
            SnapshotCacheKey,
            async cancel => await BuildSnapshotAsync(cancel),
            cancellationToken: cancellationToken
        );

        if (snapshot is null || snapshot.Length == 0)
        {
            return Result<EndpointPermissionMatch?, Error>.Failure(EndpointPermissionErrors.EndpointNotFound(path));
        }

        var normalizedPath = path;
        // Try to match in order
        foreach (var entry in snapshot)
        {
            if (!string.Equals(entry.HttpMethod ?? string.Empty, httpMethod ?? string.Empty, StringComparison.OrdinalIgnoreCase) && entry.HttpMethod is not null)
            {
                continue; // method mismatch
            }

            var m = entry.Matcher.Match(normalizedPath);
            if (m.Success)
            {
                var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in entry.Matcher.GetGroupNames())
                {
                    if (int.TryParse(name, out _))
                    {
                        continue; // skip numeric groups
                    }

                    var g = m.Groups[name];
                    if (g.Success)
                    {
                        routeValues[name] = g.Value;
                    }
                }

                return Result<EndpointPermissionMatch?, Error>.Success(
                    new EndpointPermissionMatch(entry.EndpointName, entry.Permissions.ToList(), routeValues)
                    {
                        PathTemplate = entry.PathTemplate
                    });
            }
        }

        return Result<EndpointPermissionMatch?, Error>.Failure(EndpointPermissionErrors.EndpointNotFound(path));
    }

    /// <summary>
    /// Builds a snapshot of endpoint mappings from the database.
    /// Called by HybridCache when cache is empty or invalidated.
    /// HybridCache provides stampede protection automatically.
    /// </summary>
    private async Task<MappingEntry[]> BuildSnapshotAsync(CancellationToken cancellationToken)
    {
        using var context = dbContextFactory.CreateDbContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var all = await context.EndpointMappings
            .AsNoTracking()
            .Include(e => e.Permissions)
            .ToListAsync(cancellationToken)
            ;

        var list = new List<MappingEntry>();
        foreach (var e in all)
        {
            var pathTemplateNormalized = e.PathTemplate?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(pathTemplateNormalized))
            {
                continue;
            }

            var regex = BuildRegexFromTemplate(pathTemplateNormalized);
            var pattern = regex.ToString();
            var perms = e.Permissions.Select(p => p.Key.Value).ToList();
            var specificity = ComputeSpecificity(pathTemplateNormalized);
            list.Add(new MappingEntry(e.EndpointName, pathTemplateNormalized, pattern, e.HttpMethod, perms, specificity));
        }

        // Order by HttpMethod presence first, then specificity desc
        return list.OrderByDescending(x => x.HttpMethod != null).ThenByDescending(x => x.Specificity).ToArray();
    }

    private static Regex BuildRegexFromTemplate(string template)
    {
        // Escape static text and replace placeholders
        // {name} -> (?<name>[^/]+)
        // {**name} -> (?<name>.*)
        var sb = new StringBuilder();
        sb.Append('^');
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end == -1)
                {
                    // treat as literal
                    sb.Append(Regex.Escape("{"));
                    i++;
                    continue;
                }

                var inner = template[(i + 1)..end];
                if (inner.StartsWith("**"))
                {
                    var name = inner[2..];
                    sb.Append($"(?<{name}>.*)");
                }
                else
                {
                    var name = inner;
                    sb.Append($"(?<{name}>[^/]+)");
                }
                i = end + 1;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }
        sb.Append("/?$");
        return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static int ComputeSpecificity(string template)
    {
        // Simple heuristic: static chars count - placeholder count
        var staticCount = template.Count(ch => ch is not '{' and not '}' and not '/');
        var placeholderCount = template.Count(ch => ch == '{');
        return staticCount - placeholderCount;
    }
    public async Task<IList<EndpointPermissionMap>> GetEndpointMappingsAsync(string endpointName, CancellationToken cancellationToken)
    {
        using var context = dbContextFactory.CreateDbContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var map = await context.EndpointMappings
            .AsNoTracking()
            .OrderBy(e => e.EndpointName)
            .Select(e => new EndpointPermissionMap(
                e.EndpointName,
                e.Permissions.Select(p => p.Key.Value).ToList())
            {
                PathTemplate = e.PathTemplate,
                HttpMethod = e.HttpMethod,
                Description = e.Description
            })
            .ToListAsync(cancellationToken);

        return map;
    }
    public async Task<Result<Error>> SetEndpointPermissionAsync(EndpointPermissionMap endpointPermission, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointPermission.EndpointName))
        {
            return Result<Error>.Failure(EndpointPermissionErrors.InvalidEndpointName);
        }

        using var context = dbContextFactory.CreateDbContext();

        // Convert permission strings to PermissionKey structs for EF Core query
        var permissionKeys = endpointPermission.Permissions
            .Select(p => new PermissionKey(p))
            .ToArray();

        var permissions = await context.Permissions
            .Where(p => permissionKeys.Contains(p.Key))
            .ToArrayAsync(cancellationToken);

        var missing = endpointPermission.Permissions.Except(permissions.Select(p => p.Key.Value)).ToArray();
        if (missing.Length != 0)
        {
            logger.PermissionsNotFound(endpointPermission.EndpointName, string.Join(", ", missing));
            return Result<Error>.Failure(EndpointPermissionErrors.PermissionsNotFound);
        }

        var mapping = await context.EndpointMappings
            .Include(e => e.Permissions)
            .Where(e => e.EndpointName == endpointPermission.EndpointName)
            .FirstOrDefaultAsync(cancellationToken);

        if (mapping is null)
        {
            mapping = new EndpointMapping
            {
                EndpointName = endpointPermission.EndpointName,
                Permissions = permissions.ToList()
            };
            context.EndpointMappings.Add(mapping);
        }
        else
        {
            mapping.Permissions = permissions.ToList();
        }

        await context.SaveChangesAsync(cancellationToken);

        // Invalidate cache so next request will fetch fresh data from DB
        await cache.RemoveAsync(SnapshotCacheKey, cancellationToken);

        return Result<Error>.Success();
    }

    /// <inheritdoc/>
    public Task InvalidateSnapshotAsync(CancellationToken cancellationToken) =>
        cache.RemoveAsync(SnapshotCacheKey, cancellationToken).AsTask();

    public async Task<Result<EndpointPermissionMap, Error>> CreateEndpointMappingAsync(
        string endpointName,
        string pathTemplate,
        string httpMethod,
        string? description,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.InvalidEndpointName);
        }

        if (string.IsNullOrWhiteSpace(pathTemplate))
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.InvalidPathTemplate);
        }

        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.InvalidHttpMethod);
        }

        endpointName = endpointName.Trim();
        pathTemplate = pathTemplate.Trim();
        httpMethod = httpMethod.Trim();

        using var context = dbContextFactory.CreateDbContext();

        var exists = await context.EndpointMappings
            .AsNoTracking()
            .AnyAsync(e => e.EndpointName == endpointName, cancellationToken);

        if (exists)
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.EndpointAlreadyExists(endpointName));
        }

        var mapping = new EndpointMapping
        {
            EndpointName = endpointName,
            PathTemplate = pathTemplate,
            HttpMethod = httpMethod.ToUpperInvariant(),
            Description = description
        };

        context.EndpointMappings.Add(mapping);
        await context.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync(SnapshotCacheKey, cancellationToken);

        var result = new EndpointPermissionMap(
            endpointName,
            Array.Empty<string>())
        {
            PathTemplate = pathTemplate,
            HttpMethod = httpMethod.ToUpperInvariant(),
            Description = description
        };

        return Result<EndpointPermissionMap, Error>.Success(result);
    }

    /// <summary>
    /// Removes an endpoint mapping and its associated permissions from the system.
    /// </summary>
    /// <remarks>
    /// The endpoint-to-permission join table uses foreign key constraints configured with
    /// <c>DeleteBehavior.Restrict</c>, preventing the database from cascading deletes.
    /// This method explicitly clears the permissions collection before deletion, allowing
    /// Entity Framework to issue join-table DELETE statements before the parent mapping DELETE
    /// in a single transaction, avoiding foreign key constraint violations.
    /// </remarks>
    /// <param name="endpointName">The name of the endpoint mapping to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A success result if the mapping was found and deleted; a failure result with
    /// <see cref="EndpointPermissionErrors.InvalidEndpointName"/> if the name is empty,
    /// or <see cref="EndpointPermissionErrors.EndpointNotFound(string)"/> if no mapping exists for the name.
    /// Invalidates the snapshot cache on successful deletion.
    /// </returns>
    public async Task<Result<Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return Result<Error>.Failure(EndpointPermissionErrors.InvalidEndpointName);
        }

        using var context = dbContextFactory.CreateDbContext();

        var mapping = await context.EndpointMappings
            .Include(e => e.Permissions)
            .Where(e => e.EndpointName == endpointName)
            .FirstOrDefaultAsync(cancellationToken);

        if (mapping is null)
        {
            return Result<Error>.Failure(EndpointPermissionErrors.EndpointNotFound(endpointName));
        }

        // Both EndpointMapping_Permission FKs are Restrict, so the database will not cascade the join
        // rows away with the parent: drop the associations here and EF orders the join-table DELETEs
        // ahead of the parent DELETE in one SaveChanges (as RoleService.UpdateRolePermissions does).
        mapping.Permissions.Clear();
        context.EndpointMappings.Remove(mapping);
        await context.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync(SnapshotCacheKey, cancellationToken);

        return Result<Error>.Success();
    }

    public async Task<Result<EndpointPermissionMap, Error>> GetEndpointMappingByNameAsync(
        string endpointName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpointName))
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.InvalidEndpointName);
        }

        using var context = dbContextFactory.CreateDbContext();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var mapping = await context.EndpointMappings
            .Include(e => e.Permissions)
            .Where(e => e.EndpointName == endpointName)
            .FirstOrDefaultAsync(cancellationToken);

        if (mapping is null)
        {
            return Result<EndpointPermissionMap, Error>.Failure(EndpointPermissionErrors.EndpointNotFound(endpointName));
        }

        var result = new EndpointPermissionMap(
            mapping.EndpointName,
            mapping.Permissions.Select(p => p.Key.Value).ToArray())
        {
            PathTemplate = mapping.PathTemplate,
            HttpMethod = mapping.HttpMethod,
            Description = mapping.Description
        };

        return Result<EndpointPermissionMap, Error>.Success(result);
    }
}