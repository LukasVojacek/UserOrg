using System.Diagnostics.CodeAnalysis;

using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Flows;

/// <summary>
/// Compiled resume routing for a gate: given the delivered signal and the request snapshot, returns the
/// next step message, or <c>null</c> for a terminal transition. Built by <see cref="FlowBuilder"/> from the
/// typed <see cref="IGateTransitions{TSignal}"/> delegates.
/// </summary>
internal delegate StepRequest? GateResume(object signal, IRequestSnapshot snapshot);

/// <summary>A gate's compiled metadata: the signal type it awaits and its resume routing.</summary>
internal sealed record GateDescriptor(Type SignalType, GateResume Resume);

/// <summary>
/// Implemented by strategies that declare gates, so the engine can resume a request parked at a gate.
/// Hand-written strategies without gates need not implement this.
/// </summary>
internal interface IGatedStrategy
{
    /// <summary>Returns the descriptor for <paramref name="gateName"/>, or <c>false</c> if no such gate exists.</summary>
    bool TryGetGate(string gateName, [NotNullWhen(true)] out GateDescriptor? descriptor);
}