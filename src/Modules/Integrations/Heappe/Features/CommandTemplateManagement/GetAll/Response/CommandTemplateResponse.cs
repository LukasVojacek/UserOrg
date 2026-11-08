namespace Heappe.Features.CommandTemplateManagement.GetAll.Response;

/// <summary>
/// Represents the response data for a command template, including its unique identifier, name, type, and current state.
/// </summary>
/// <param name="Id">The unique identifier of the command template.</param>
/// <param name="Name">The display name of the command template.</param>
/// <param name="Type">The type of the command template.</param>
/// <param name="State">The current state of the command template.</param>
/// <param name="Frozen">True when at least one of the template's partitions sits on a location
/// frozen by an active kill-switch. Never accompanied by a reason — the incident reason is
/// admin-only (ADR-0077).</param>
internal sealed record CommandTemplateResponse
(
    Guid Id,
    string Name,
    ECommandTemplateTypeResponse Type,
    ECommandTemplateStateResponse State,
    bool Frozen = false
);