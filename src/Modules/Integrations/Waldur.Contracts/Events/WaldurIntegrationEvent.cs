namespace Waldur.Contracts.Events;

/// <summary>
/// Base type for every fat CRUD event crossing the Waldur sync seam (ADR-0054). Carries the
/// producer identity common to all events; entity-specific state lives in the derived payloads.
/// </summary>
public abstract record WaldurIntegrationEvent
{
    /// <summary>Instance name / OTel tag ("EFP", "Puhuri", …).</summary>
    public required string Instance { get; init; }

    /// <summary>Upsert-key discriminator, e.g. "EFP-Waldur" / "Puhuri-Waldur".</summary>
    public required string ServiceName { get; init; }
}