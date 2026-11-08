namespace RemI.Features.Projects;

/// <summary>
/// A project as the RemI service sees it. Mirrors the field selection of the v2 project list
/// (<c>GET /api/v2/Projects</c>): identity is the project short name, not a database key.
/// </summary>
public sealed record ProjectResponse(
    string ShortName,
    string Name,
    string? Email,
    string? ExternalId,
    string Mode,
    string ServiceName,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate);