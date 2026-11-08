using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;

namespace UserOrg.Gateway.Features.Authorization;

/// <summary>
/// High-performance permission matcher with regex caching for template-based permissions.
/// Thread-safe and optimized for high-throughput scenarios.
/// </summary>
internal sealed partial class PermissionMatcher : IPermissionMatcher
{
    private readonly ILogger<PermissionMatcher> _logger;

    // Cache compiled regex patterns per permission template for performance
    private readonly ConcurrentDictionary<string, Regex> _regexCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ActivitySource ActivitySource = new("UserOrg.Gateway.Authorization");
    private static readonly Meter Meter = new("UserOrg.Gateway.Authorization");
    private static readonly Counter<long> CacheHitCounter = Meter.CreateCounter<long>("permission_matcher.cache.hits", description: "Number of regex cache hits");
    private static readonly Counter<long> CacheMissCounter = Meter.CreateCounter<long>("permission_matcher.cache.misses", description: "Number of regex cache misses");
    private static readonly Counter<long> MatchCounter = Meter.CreateCounter<long>("permission_matcher.matches", description: "Number of permission matches");
    private static readonly Counter<long> NoMatchCounter = Meter.CreateCounter<long>("permission_matcher.no_matches", description: "Number of permission non-matches");

    public PermissionMatcher(ILogger<PermissionMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if user has at least one permission matching any of the required templates.
    /// Literal templates are matched exactly, templates with placeholders use regex wildcard matching.
    /// </summary>
    public bool HasRequiredPermission(IReadOnlyList<string> permissionTemplates, HashSet<string> userPermissions)
        => HasRequiredPermission(permissionTemplates, userPermissions, null);

    /// <inheritdoc/>
    public bool HasRequiredPermission(IReadOnlyList<string> permissionTemplates, HashSet<string> userPermissions, IReadOnlyDictionary<string, string>? routeValues)
    {
        using var activity = ActivitySource.StartActivity("PermissionMatcher.HasRequiredPermission");
        TagRequest(activity, permissionTemplates, userPermissions, routeValues);

        foreach (var template in permissionTemplates)
        {
            if (MatchesTemplate(Resolve(template, routeValues), userPermissions, activity))
            {
                return true;
            }
        }

        activity?.SetTag("match_type", "none");
        NoMatchCounter.Add(1);
        LogNoPermissionMatched(_logger, permissionTemplates, userPermissions);
        return false;
    }

    private static void TagRequest(
        Activity? activity,
        IReadOnlyList<string> permissionTemplates,
        HashSet<string> userPermissions,
        IReadOnlyDictionary<string, string>? routeValues)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag("template_count", permissionTemplates.Count);
        activity.SetTag("user_permission_count", userPermissions.Count);
        activity.SetTag("route_values_count", routeValues?.Count ?? 0);
    }

    // Substitute route values into placeholders before matching.
    // E.g. template "perm:project:{ProjectShortName}:admin" + routeValues { "ProjectShortName": "myproject" }
    // → "perm:project:myproject:admin" (literal – forces exact-project check).
    private static string Resolve(string template, IReadOnlyDictionary<string, string>? routeValues)
        => routeValues is { Count: > 0 }
            ? SubstitutePlaceholders(template, routeValues)
            : template;

    private bool MatchesTemplate(string resolved, HashSet<string> userPermissions, Activity? activity)
        => resolved.Contains('{', StringComparison.Ordinal)
            ? MatchesWildcard(resolved, userPermissions, activity)
            : MatchesLiteral(resolved, userPermissions, activity);

    // Literal permission - exact match (case-insensitive)
    private bool MatchesLiteral(string resolved, HashSet<string> userPermissions, Activity? activity)
    {
        if (!userPermissions.Any(up => string.Equals(up, resolved, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        activity?.SetTag("match_type", "literal");
        activity?.SetTag("matched_template", resolved);
        MatchCounter.Add(1, new KeyValuePair<string, object?>("type", "literal"));
        LogLiteralMatch(_logger, resolved);
        return true;
    }

    // Template still has unresolved placeholders – regex wildcard matching.
    private bool MatchesWildcard(string resolved, HashSet<string> userPermissions, Activity? activity)
    {
        var cacheHit = RecordCacheLookup(resolved);
        var regex = _regexCache.GetOrAdd(resolved, CreateRegexForTemplate);

        var userPerm = userPermissions.FirstOrDefault(regex.IsMatch);
        if (userPerm is null)
        {
            return false;
        }

        activity?.SetTag("match_type", "template");
        activity?.SetTag("matched_template", resolved);
        activity?.SetTag("matched_permission", userPerm);
        activity?.SetTag("cache_hit", cacheHit);
        MatchCounter.Add(1, new KeyValuePair<string, object?>("type", "template"));
        LogTemplateMatch(_logger, resolved, userPerm, cacheHit);
        return true;
    }

    private bool RecordCacheLookup(string resolved)
    {
        var cacheHit = _regexCache.ContainsKey(resolved);
        if (cacheHit)
        {
            CacheHitCounter.Add(1);
        }
        else
        {
            CacheMissCounter.Add(1);
            LogRegexCacheMiss(_logger, resolved, _regexCache.Count);
        }

        return cacheHit;
    }

    /// <summary>
    /// Substitutes route values into permission template placeholders.
    /// E.g. template <c>perm:project:{ProjectShortName}:read</c> + routeValues <c>{ "ProjectShortName": "myproject" }</c>
    /// → <c>perm:project:myproject:read</c>.
    /// Unrecognised placeholders are left as-is for regex wildcard fallback.
    /// </summary>
    private static string SubstitutePlaceholders(string template, IReadOnlyDictionary<string, string> routeValues)
    {
        var result = template;
        foreach (var (key, value) in routeValues)
        {
            result = result.Replace($"{{{key}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// Converts a permission template like "perm:project:{name}:read" to a compiled regex pattern.
    /// Placeholders {name} are replaced with [A-Za-z0-9_]+ (letters, digits, underscore).
    /// </summary>
    private static Regex CreateRegexForTemplate(string template)
    {
        // Build regex by escaping literal segments and replacing placeholders with the wildcard pattern.
        // This is more robust than escaping the whole template and applying a single regex replace.
        var sb = new System.Text.StringBuilder();
        var wildcard = "[A-Za-z0-9_]+";
        var last = 0;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] == '{')
            {
                // append escaped literal part before '{'
                if (i > last)
                {
                    sb.Append(Regex.Escape(template.Substring(last, i - last)));
                }

                var end = template.IndexOf('}', i + 1);
                if (end == -1)
                {
                    // malformed template - treat remaining as literal
                    sb.Append(Regex.Escape(template.Substring(i)));
                    last = template.Length;
                    break;
                }

                sb.Append(wildcard);
                i = end;
                last = i + 1;
            }
        }

        if (last < template.Length)
        {
            sb.Append(Regex.Escape(template.Substring(last)));
        }

        var fullPattern = $"^{sb}$";

        return new Regex(fullPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Permission matched (literal): {Template}")]
    private static partial void LogLiteralMatch(ILogger logger, string template);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Regex cache miss for template: {Template}. Cache size: {CacheSize}")]
    private static partial void LogRegexCacheMiss(ILogger logger, string template, int cacheSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Permission matched (template): Template={Template}, UserPermission={UserPermission}, CacheHit={CacheHit}")]
    private static partial void LogTemplateMatch(ILogger logger, string template, string userPermission, bool cacheHit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "No permission matched. Templates: {Templates}, UserPermissions: {UserPermissions}")]
    private static partial void LogNoPermissionMatched(ILogger logger, IReadOnlyList<string> templates, IReadOnlySet<string> userPermissions);
}