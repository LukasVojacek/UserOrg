namespace Core.DataAccess.Queries.Contracts;

/// <summary>
/// Read DTO for a country reference row. Mirrors the legacy <c>CountryModel</c> projection, which
/// deliberately exposes the entity's <c>Name</c> column as <see cref="Code"/> and the
/// <c>Description</c> column as <see cref="Name"/>.
/// </summary>
public sealed record CountrySummary(string Code, string? Name);