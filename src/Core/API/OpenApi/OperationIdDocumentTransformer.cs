using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace UserOrganizationManagement.API.OpenApi;

/// <summary>
/// Gives every operation that lacks one a deterministic, unique, stable <c>operationId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Spectral's <c>operation-operationId</c> rule is an error in <c>.spectral.yml</c>, and operationIds are
/// the method names client generators emit, so they are part of the API contract. This transformer only
/// ever <em>adds</em> ids: an explicit <c>.WithName(...)</c> has already populated
/// <see cref="OpenApiOperation.OperationId"/> by the time it runs and is never overwritten.
/// </para>
/// <para>
/// Why document level rather than operation level: uniqueness cannot be decided from a single operation.
/// An operation-level transformer sees neither the ids explicitly assigned elsewhere in the document nor
/// its peers, so it could neither avoid shadowing a <c>.WithName(...)</c> id nor resolve a collision
/// between two generated ids. The whole-document view makes both a single, order-independent pass.
/// </para>
/// <para>
/// Determinism: the id is a pure function of the HTTP method and the route template. Dictionary
/// enumeration order is not part of the input — the entries are sorted by (path, method) with ordinal
/// comparison before anything is assigned, so the rare numeric disambiguation suffix is stable too. No
/// hashes, no GUIDs, no counters over unordered collections.
/// </para>
/// <para>
/// Ordering: registered before <see cref="DocumentPathGroupFilter"/> and
/// <see cref="FeatureFlagDocumentTransformer"/> so the pass runs over the complete, unfiltered operation
/// set. The legacy "User" and "Administration" documents are generated from the same V1Api-gated endpoint set
/// and are only split afterwards, so an operation keeps the same id in either. The "Service" document selects
/// the ungated legacy endpoints before this pass: an operation there keeps the id it had in the legacy pair
/// because the base id depends on method and route alone, and a suffix appears only on a collision within one
/// document.
/// </para>
/// </remarks>
internal sealed class OperationIdDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Paths is null || document.Paths.Count == 0)
        {
            return Task.CompletedTask;
        }

        var ordered = document.Paths
            .Where(path => path.Value?.Operations is { Count: > 0 })
            .OrderBy(path => path.Key, StringComparer.Ordinal)
            .SelectMany(path => path.Value.Operations!
                .OrderBy(op => op.Key.Method, StringComparer.Ordinal)
                .Select(op => (Path: path.Key, Method: op.Key.Method, Operation: op.Value)))
            .ToList();

        // Pass 1: reserve every id that was assigned explicitly (.WithName) so a generated id can never
        // shadow one, whatever order the operations happen to be visited in.
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in ordered)
        {
            if (!string.IsNullOrWhiteSpace(entry.Operation.OperationId))
            {
                taken.Add(entry.Operation.OperationId);
            }
        }

        foreach (var entry in ordered)
        {
            if (!string.IsNullOrWhiteSpace(entry.Operation.OperationId))
            {
                continue;
            }

            entry.Operation.OperationId = Reserve(BuildOperationId(entry.Method, entry.Path), taken);
        }

        return Task.CompletedTask;
    }

    private static string Reserve(string candidate, HashSet<string> taken)
    {
        if (taken.Add(candidate))
        {
            return candidate;
        }

        // The base id is injective over (method, route template) in practice, so this is a safety net for
        // the degenerate cases (a literal segment spelled like a route parameter, two templates differing
        // only in punctuation). The suffix is driven by the ordinal sort above, not by enumeration order.
        for (var suffix = 2; ; suffix++)
        {
            var disambiguated = candidate + suffix.ToString(CultureInfo.InvariantCulture);
            if (taken.Add(disambiguated))
            {
                return disambiguated;
            }
        }
    }

    /// <summary>
    /// Builds a PascalCase identifier from the HTTP method and route template, e.g.
    /// <c>GET /api/Provider/{Id:GUID}</c> becomes <c>GetApiProviderById</c>. The result is alphanumeric,
    /// so it is usable verbatim as a generated client method name.
    /// </summary>
    internal static string BuildOperationId(string httpMethod, string path)
    {
        var builder = new StringBuilder();
        // "DELETE" -> "Delete": the method arrives upper-cased and AppendPascalCase preserves inner casing.
        AppendPascalCase(builder, httpMethod.ToLowerInvariant());

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment.Length > 2 && segment[0] == '{' && segment[^1] == '}')
            {
                builder.Append("By");
                AppendPascalCase(builder, RouteParameterName(segment[1..^1]));
            }
            else
            {
                AppendPascalCase(builder, segment);
            }
        }

        return builder.Length == 0 ? "Operation" : builder.ToString();
    }

    // Strips the route-constraint (":guid"), default ("=value"), catch-all ("*") and optional ("?")
    // decorations so that "{Id:GUID}" and "{Id}" describe the same parameter name.
    private static string RouteParameterName(string token)
    {
        var name = token;

        var colon = name.IndexOf(':', StringComparison.Ordinal);
        if (colon >= 0)
        {
            name = name[..colon];
        }

        var equals = name.IndexOf('=', StringComparison.Ordinal);
        if (equals >= 0)
        {
            name = name[..equals];
        }

        return name.TrimStart('*').TrimEnd('?');
    }

    private static void AppendPascalCase(StringBuilder builder, string value)
    {
        var startOfWord = true;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                startOfWord = true;
                continue;
            }

            if (startOfWord)
            {
                builder.Append(char.ToUpperInvariant(character));
                startOfWord = false;
            }
            else
            {
                builder.Append(character);
            }
        }
    }
}