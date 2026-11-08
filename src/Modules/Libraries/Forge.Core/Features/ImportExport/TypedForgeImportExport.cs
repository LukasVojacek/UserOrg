using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using UserOrg.Shared;

namespace Forge.Core.Features.ImportExport;

/// <summary>
/// Base type for feature importers. It owns the public import entry point and guarantees that
/// every import is followed by cache invalidation, so an importer cannot leave a cache serving
/// data the import has just replaced (ADR-0084).
/// </summary>
/// <remarks>
/// Derived types implement <see cref="ImportCoreAsync"/> for the payload handling and
/// <see cref="InvalidateAffectedCachesAsync"/> for the caches that import affects. Both are
/// abstract: an importer with nothing to invalidate must say so explicitly rather than omit it.
/// <see cref="DeserializeAndImportAsync"/> offers the typed
/// JsonElement -&gt; TPayload -&gt; ImportAsync(TPayload, ...) path to importers that want it.
/// </remarks>
/// <typeparam name="TPayload">Payload DTO type for the feature import.</typeparam>
public abstract class TypedForgeImportExport<TPayload> : IForgeImportExport
{
    private readonly JsonSerializerContext? _serializerContext;

    protected TypedForgeImportExport(JsonSerializerContext? serializerContext = null)
    {
        _serializerContext = serializerContext;
    }

    private static bool PayloadAppearsIncomplete<T>(T payload, JsonElement source)
    {
        // Heuristic: if source has a property 'items' but payload.Items is null (common with IEnumerable),
        // we'll treat it as incomplete. This keeps tests stable when source-gen produces null for
        // interface-typed properties.
        try
        {
            if (source.ValueKind != JsonValueKind.Object)
                return false;

            if (source.TryGetProperty("items", out var itemsProp))
            {
                var prop = typeof(T).GetProperty("Items", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is not null)
                {
                    var val = prop.GetValue(payload);
                    if (val is null && itemsProp.ValueKind == JsonValueKind.Array)
                        return true;
                }
            }
        }
        catch
        {
            // best-effort; don't throw from helper
        }

        return false;
    }

    /// <inheritdoc />
    public abstract string FeatureName { get; }

    /// <inheritdoc />
    public abstract Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Imports a strongly-typed payload for the feature. Not part of the public interface: the
    /// only invalidation-safe way to import is <see cref="ImportAsync(JsonElement, ImportOptions, CancellationToken)"/>,
    /// which every caller reaches through <see cref="IForgeImportExport"/>.
    /// </summary>
    /// <param name="payload">The typed payload to import.</param>
    /// <param name="options">Options controlling import behavior.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The import result with statistics and any errors.</returns>
    protected abstract Task<ImportResult> ImportAsync(TPayload payload, ImportOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Imports the payload and then invalidates the caches the import affects.
    /// </summary>
    /// <param name="payload">The JSON element containing the feature's data to import.</param>
    /// <param name="options">Options controlling import behavior.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>
    /// The result of <see cref="ImportCoreAsync"/>, with a failed invalidation appended to its
    /// errors.
    /// </returns>
    public async Task<ImportResult> ImportAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
    {
        ImportResult result;
        try
        {
            result = await ImportCoreAsync(payload, options, cancellationToken);
        }
        catch
        {
            // ADR-0084 §2: a throwing core could still have written rows before failing, so the
            // cache must not be assumed clean just because no ImportResult came back.
            await TryInvalidateAsync();
            throw;
        }

        // ADR-0084: invalidate after every import, not only a successful one — a partial import
        // reports errors yet has already written the rows the cache is now stale against. The
        // write already committed, so this eviction must not be cancellable by the request token.
        try
        {
            await InvalidateAffectedCachesAsync(CancellationToken.None);
            return result;
        }
        catch (Exception ex)
        {
            // ADR-0084: the write already happened, so the caller must learn the cache is stale
            // from the result rather than from an exception that discards the counts.
            var errors = (result.Errors ?? []).Append($"Cache invalidation failed after import: {ex.Message}");

            return result with { Success = false, Errors = errors.ToList() };
        }
    }

    private async Task TryInvalidateAsync()
    {
        try
        {
            await InvalidateAffectedCachesAsync(CancellationToken.None);
        }
        catch
        {
            // Best-effort: ImportCoreAsync's own exception takes priority and must reach the
            // caller unmasked.
        }
    }

    /// <summary>
    /// Imports the feature's payload. Called by <see cref="ImportAsync(JsonElement, ImportOptions, CancellationToken)"/>.
    /// </summary>
    /// <param name="payload">The JSON element containing the feature's data to import.</param>
    /// <param name="options">Options controlling import behavior.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The import result with statistics and any errors.</returns>
    protected abstract Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken);

    /// <summary>
    /// Invalidates every cache whose contents an import of this feature can invalidate. Called
    /// after each import, including one that reported errors. An importer whose data no read path
    /// caches implements this as an explicit no-op.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A task representing the async invalidation.</returns>
    protected abstract Task InvalidateAffectedCachesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deserializes the payload into <typeparamref name="TPayload"/> and hands it to
    /// <see cref="ImportAsync(TPayload, ImportOptions, CancellationToken)"/>. Available to a
    /// derived <see cref="ImportCoreAsync"/> that wants the typed path instead of reading the
    /// <see cref="JsonElement"/> itself.
    /// </summary>
    /// <param name="payload">The JSON element containing the feature's data to import.</param>
    /// <param name="options">Options controlling import behavior.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>The typed import's result, or a deserialization error.</returns>
    protected async Task<ImportResult> DeserializeAndImportAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
    {
        try
        {
            var json = payload.GetRawText();

            if (_serializerContext is not null)
            {
                var des = JsonSerializer.Deserialize(json, typeof(TPayload), _serializerContext);
                if (des is not TPayload typed)
                    return new ImportResult(false, Errors: new[] { "Failed to deserialize payload" });

                return await ImportAsync(typed, options, cancellationToken);
            }

            // Fallback to reflection-based deserialization when no context is provided
            var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // Ensure shared converters (value objects like PermissionKey) are available during deserialization
            jsOptions.Converters.Add(new PermissionKeyJsonConverter());
            var deserialized = JsonSerializer.Deserialize<TPayload>(json, jsOptions);
            if (deserialized is not TPayload typedFallback)
                return new ImportResult(false, Errors: new[] { "Failed to deserialize payload" });

            return await ImportAsync(typedFallback, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            // Keep the error simple; per-feature implementations can log detailed reasons
            return new ImportResult(false, Errors: new[] { "Invalid JSON payload", ex.Message });
        }
    }
}