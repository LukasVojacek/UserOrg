namespace Core.Features.Provider.GetProvider;

internal sealed record LocationRecord(string Name, string Endpoint, string? Description, bool IsPrivate, bool IsShared);

internal sealed record GetProviderResponse(
    string Name,
    string? Description,
    string? SupportEmail,
    string? ExternalId,
    string Mode,
    string ServiceName,
    bool IsPrivate,
    string CountryCode,
    bool IsDeleted,
    IReadOnlyList<LocationRecord> Locations)
{
    internal static GetProviderResponse From(ProviderDetail provider) => new(
        provider.Name.Value,
        provider.Description?.Value is { Length: > 0 } description ? description : null,
        string.IsNullOrEmpty(provider.SupportEmail?.Value) ? null : provider.SupportEmail?.Value,
        provider.ExternalId?.Value,
        provider.Mode.ToString(),
        provider.ServiceName.Value,
        provider.IsPrivate,
        provider.CountryCode,
        provider.IsDeleted,
        [.. provider.Locations.Select(l => new LocationRecord(l.Name.Value, l.Endpoint, l.Description, l.IsPrivate, l.IsShared))]
    );
}