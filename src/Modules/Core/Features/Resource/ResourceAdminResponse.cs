using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Features.Resource;

internal sealed record ResourceAdminResponse(
    string Name,
    string? ProviderResourceName,
    string? Description,
    string? PrimaryInvestigatorEmail,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    string OperationMode,
    string? LicenseUrl,
    EProjectResourceState State,
    string? CreatedByEmail,
    IReadOnlyList<ResourceAssignmentResponse> Assignments)
    : ResourceResponse(Name, ProviderResourceName, Description, PrimaryInvestigatorEmail, StartDate, EndDate, OperationMode, LicenseUrl, State, Assignments)
{
    internal static new ResourceAdminResponse From(ResourceSummary r) => new(
        r.Name,
        r.ProviderResourceName,
        r.Description,
        r.PrimaryInvestigatorEmail,
        r.StartDate,
        r.EndDate,
        r.OperationMode.ToString(),
        r.LicenseUrl,
        r.EProjectResourceState,
        r.CreatedByEmail?.Value.ToString(),
        r.Assignments.Select(ResourceAssignmentResponse.From).ToList());
}