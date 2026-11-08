namespace Waldur.Contracts.Tests;

/// <summary>
/// Pinned SHA-256 hex digests for fixed sample payloads. Filled from the first green run; a change
/// here means the canonical serialization changed and every stored SHA breadcrumb would be
/// invalidated — treat with care.
/// </summary>
internal static class KnownVectors
{
    internal const string SampleProject = "10c65a98c0cc03c9892dd4dc8f1e18fe5660a13ad687d96f77fe81f9d6293bc7";
}
