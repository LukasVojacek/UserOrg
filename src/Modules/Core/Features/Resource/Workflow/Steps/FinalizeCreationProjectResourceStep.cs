using System.Text.Json;
using System.Text.Json.Serialization;

using WorkflowEngine.Contracts.Abstractions;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

namespace Core.Features.Resource.Workflow.Steps;

/// <summary>
/// T-055-04: carries the provider's natural key, not the aggregate. See <see cref="CheckHeappeStep"/> for why
/// the type name, the state name and the required <see cref="ProviderName"/> are all part of the persisted
/// contract.
/// </summary>
internal sealed record FinalizeCreationProjectResourceStep(
    Guid RequestId,
    [property: JsonRequired] ProviderName ProviderName,
    string Name,
    string PrimaryInvestigatorEmail,
    List<LocationAssignment> AssignedLocations,
    IReadOnlyList<ResolvedLocationRef> ResolvedLocations,
    JsonDocument? Data
) : StepRequest(RequestId, "FinalizeCreationProjectResource");