using System.Text.Json.Serialization;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow.Steps;

/// <summary>
/// T-055-04: carries the provider's natural key, not the aggregate. See <see cref="CheckHeappeStep"/> for why
/// the type name, the state name and the required <see cref="ProviderName"/> are all part of the persisted
/// contract.
/// </summary>
internal sealed record InitializeHeappeStep(
    Guid RequestId,
    [property: JsonRequired] ProviderName ProviderName,
    CreateResourceOnLocation.CreateResourceOnLocationEndpoint.Request Data,
    IReadOnlyList<ResolvedLocationRef> ResolvedLocations
    ) : StepRequest(RequestId, "InitializeHeappe");