using Microsoft.Extensions.DependencyInjection;

namespace WorkflowEngine.Core.Flows;

/// <summary>
/// The validated output of <see cref="FlowBuilder.Build"/>: everything <c>AddFlow</c> needs to wire a flow
/// into the engine — its name, derived entry step, per-step handler registrations, and compiled transitions.
/// </summary>
internal sealed class CompiledFlow
{
    public CompiledFlow(
        string name,
        Type entryType,
        IReadOnlyList<Action<IServiceCollection>> handlerRegistrations,
        IReadOnlyDictionary<Type, CompiledTransition> transitions,
        IReadOnlyDictionary<string, GateDescriptor> gates)
    {
        Name = name;
        EntryType = entryType;
        HandlerRegistrations = handlerRegistrations;
        Transitions = transitions;
        Gates = gates;
    }

    /// <summary>Flow name; becomes the synthesized strategy's name.</summary>
    public string Name { get; }

    /// <summary>The single entry step, derived as the step that is never a transition target.</summary>
    public Type EntryType { get; }

    /// <summary>One DI registration per declared step, binding its handler.</summary>
    public IReadOnlyList<Action<IServiceCollection>> HandlerRegistrations { get; }

    /// <summary>Compiled transition per source step type.</summary>
    public IReadOnlyDictionary<Type, CompiledTransition> Transitions { get; }

    /// <summary>Compiled gate descriptor per gate name.</summary>
    public IReadOnlyDictionary<string, GateDescriptor> Gates { get; }
}