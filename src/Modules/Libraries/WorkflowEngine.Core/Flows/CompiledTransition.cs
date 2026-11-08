using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Flows;

/// <summary>
/// A compiled, type-erased transition out of one state: given the completed step, its result, and the
/// request snapshot, returns the next step message or <c>null</c> for a terminal transition.
/// Built by <see cref="FlowBuilder"/> from the typed routing delegates and executed by
/// <see cref="CompiledFlowStrategy"/>.
/// </summary>
internal delegate StepRequest? CompiledTransition(
    StepRequest completedStep,
    StepResult? stepResult,
    IRequestSnapshot snapshot);