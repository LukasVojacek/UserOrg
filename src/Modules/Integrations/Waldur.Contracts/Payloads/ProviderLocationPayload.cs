using UserOrg.Shared.Hashing;

namespace Waldur.Contracts.Payloads;

/// <summary>
/// A provider location, derived from the customer's public offerings (ADR-0054/A3) — not raw API
/// data. Offerings are grouped by SSH-login URL; the name is the longest common prefix of the
/// grouped offering names, the endpoint has the <c>ssh://</c> scheme stripped for HPC.
/// </summary>
/// <param name="Name">Derived location name (longest common prefix, trimmed of trailing '-').</param>
/// <param name="Endpoint">Normalized login endpoint (scheme stripped for HPC).</param>
/// <param name="LocationType">Location type name (offering <c>category_title</c>), e.g. "HPC".</param>
/// <param name="Description">Optional description from the first offering in the group.</param>
/// <param name="IsShared">Whether the underlying offerings are shared.</param>
/// <param name="IsDeleted">True when every offering in the group is archived.</param>
[GenerateIdentityHash]
public sealed partial record ProviderLocationPayload(
    string Name,
    string Endpoint,
    string LocationType,
    string? Description,
    bool IsShared,
    bool IsDeleted);