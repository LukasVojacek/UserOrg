using System.Text.Json.Serialization;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow.Steps;

/// <summary>
/// T-055-04: carries the provider's natural key, not the aggregate — the handler reloads the provider in its own
/// scope. See <see cref="CheckHeappeStep"/> for why the type name, the state name and the required
/// <see cref="ProviderName"/> are all part of the persisted contract.
/// </summary>
internal sealed record InitializeProjectResourceStep(
    Guid RequestId,
    [property: JsonRequired] ProviderName ProviderName,
    CreateResourceOnLocation.CreateResourceOnLocationEndpoint.Request Data,
    IReadOnlyList<ResolvedLocationRef> ResolvedLocations,
    UserId? CreatedBy = null
    ) : StepRequest(RequestId, "InitializeProjectResource");