using System.Text.Json;

namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Base record for all typed step messages in a workflow strategy.
/// Each concrete subtype represents one domain state in the flow and carries
/// only the data its handler needs — nothing more.
/// </summary>
/// <param name="RequestId">Identifies the owning request aggregate.</param>
/// <param name="StateName">Domain state name as defined by the strategy (e.g. <c>"CallHEAppE"</c>).</param>
public abstract record StepRequest(Guid RequestId, string StateName);