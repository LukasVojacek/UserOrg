using Core.Features.ResourceRequest.Flow;

namespace Core.Features.ResourceRequest.PublicAction;

/// <summary>
/// The shared token-resolve step behind both public action-link endpoints (ADR-0064): hashes the plaintext
/// token and looks up the request whose live hash matches. Returns null for every non-resolvable token — empty,
/// unknown, expired, or already-used — so callers collapse them all to the one uniform 404
/// (<see cref="ResourceRequestActionProblems.ActionNotFound"/>) and token validity never leaks.
/// </summary>
internal static class ResourceRequestActionTokenResolver
{
    extension(IProjectResourceRequestQuery query)
    {
        /// <summary>
        /// Hashes the provided token and queries for a live, unexpired action-token hash match.
        /// Returns null for any empty, unknown, expired, or already-cleared token, enabling callers
        /// to reply with an identical 404 regardless of the failure cause (ADR-0064 anti-leak).
        /// </summary>
        /// <param name="token">Plaintext action-link token (may be null or whitespace).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Request detail if a live matching token hash exists; null otherwise.</returns>
        internal async Task<ResourceRequestDetail?> FindByActionTokenAsync(string? token, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(token))
                return null;

            var tokenHash = ResourceRequestToken.Hash(token);
            return await query.FindByValidTokenHashAsync(tokenHash, DateTimeOffset.UtcNow, ct);
        }
    }
}