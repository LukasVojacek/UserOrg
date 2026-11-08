using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Tests.Helpers;

/// <summary>
/// Minimal concrete <see cref="StepRequest"/> used in handler retry tests to exercise
/// the message-deserialization path inside <c>RetryLastStepHandler</c> and <c>RetryStepHandler</c>.
/// Must be <c>public</c> so <c>System.Text.Json</c> can deserialize it and
/// <c>Type.GetType</c> can resolve it by assembly-qualified name.
/// </summary>
public sealed record TestStepRequest(Guid RequestId, string StateName)
    : StepRequest(RequestId, StateName);
