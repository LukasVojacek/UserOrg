using System.Text.Json.Serialization;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow.Steps;

/// <summary>
/// T-055-04: carries the provider's natural key and plain location refs, never the aggregate. The payload is
/// persisted in <c>RequestSteps.InputData</c> and replayed after a crash, so it has to survive a
/// System.Text.Json round trip; the CLR type name, its namespace, its assembly and the <c>"CheckHeappe"</c>
/// state name are all part of that persisted contract and must not move.
/// <para>
/// <see cref="ProviderName"/> is <c>[JsonRequired]</c> so a pre-T-055-04 row — which carries an inline
/// <c>Provider</c> aggregate and no natural key — fails replay loudly with a <c>JsonException</c> instead of
/// quietly deserialising to <c>default(ProviderName)</c> and sending the handlers after a provider named null.
/// </para>
/// </summary>
internal sealed record CheckHeappeStep(
    Guid RequestId,
    [property: JsonRequired] ProviderName ProviderName,
    CreateResourceOnLocation.CreateResourceOnLocationEndpoint.Request Data,
    IReadOnlyList<ResolvedLocationRef> ResolvedLocations,
    UserId? CreatedBy = null
) : StepRequest(RequestId, "CheckHeappe");