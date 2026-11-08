using System.Diagnostics.CodeAnalysis;

using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Flows;

/// <summary>
/// An <see cref="IRequestStrategy"/> synthesized from a <see cref="CompiledFlow"/>. Dispatches the
/// engine's pure <see cref="Transition"/> call to the compiled transition for the completed step's type,
/// and exposes its gates via <see cref="IGatedStrategy"/> so a parked request can be resumed.
/// </summary>
internal sealed class CompiledFlowStrategy : IRequestStrategy, IGatedStrategy
{
    private readonly IReadOnlyDictionary<Type, CompiledTransition> _transitions;
    private readonly IReadOnlyDictionary<string, GateDescriptor> _gates;

    public CompiledFlowStrategy(
        string name,
        Type initialStepType,
        IReadOnlyDictionary<Type, CompiledTransition> transitions,
        IReadOnlyDictionary<string, GateDescriptor> gates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(initialStepType);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(gates);

        Name = name;
        InitialStepType = initialStepType;
        _transitions = transitions;
        _gates = gates;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public Type InitialStepType { get; }

    /// <inheritdoc/>
    public StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request)
    {
        ArgumentNullException.ThrowIfNull(completedStep);

        var stepType = completedStep.GetType();
        if (!_transitions.TryGetValue(stepType, out var transition))
        {
            throw new InvalidOperationException(
                $"Flow '{Name}' has no transition declared for step '{stepType.Name}'.");
        }

        return transition(completedStep, stepResult, request);
    }

    /// <inheritdoc/>
    public bool TryGetGate(string gateName, [NotNullWhen(true)] out GateDescriptor? descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateName);
        return _gates.TryGetValue(gateName, out descriptor);
    }
}