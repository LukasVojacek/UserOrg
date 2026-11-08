using Microsoft.Extensions.DependencyInjection;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.DependencyInjection;

namespace WorkflowEngine.Core.Flows;

/// <summary>
/// Accumulates a flow definition from the fluent <see cref="IFlowBuilder"/> calls and compiles it,
/// with startup validation, into a <see cref="CompiledFlow"/>. The flow graph has two node kinds —
/// step nodes (keyed by <see cref="Type"/>) and gate nodes (keyed by name) — connected by edges that
/// reachability and entry derivation are validated over.
/// </summary>
internal sealed class FlowBuilder : IFlowBuilder
{
    private readonly List<Action<IServiceCollection>> _handlerRegistrations = [];
    private readonly HashSet<Type> _declaredSteps = [];
    private readonly HashSet<Type> _stepsWithTransition = [];
    private readonly Dictionary<Type, CompiledTransition> _transitions = [];
    private readonly Dictionary<string, Type> _gateSignalTypes = [];
    private readonly Dictionary<string, GateResume> _gateResumes = [];
    private readonly Dictionary<string, Type> _gateResumeSignalTypes = [];
    private readonly Dictionary<object, HashSet<object>> _edges = [];
    private string? _flowName;

    public IFlowBuilder Named(string flowName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowName);
        _flowName = flowName;
        return this;
    }

    public IFlowBuilder Step<TReq, THandler>()
        where TReq : StepRequest
        where THandler : class, IStepHandler<TReq>
    {
        if (!_declaredSteps.Add(typeof(TReq)))
        {
            throw new FlowDefinitionException($"Step '{typeof(TReq).Name}' is declared more than once.");
        }

        _handlerRegistrations.Add(static services => services.AddStepHandler<TReq, THandler>());
        return this;
    }

    public IFlowBuilder Gate<TSignal>(string gateName) where TSignal : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateName);
        if (!_gateSignalTypes.TryAdd(gateName, typeof(TSignal)))
        {
            throw new FlowDefinitionException($"Gate '{gateName}' is declared more than once.");
        }

        return this;
    }

    public ITransitionSource<TReq> From<TReq>() where TReq : StepRequest
        => new TransitionSource<TReq>(this);

    public IGateTransitions<TSignal> FromGate<TSignal>(string gateName) where TSignal : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateName);
        return new GateTransitions<TSignal>(this, gateName);
    }

    /// <summary>Reserves a step's single outgoing transition, guarding against double declaration.</summary>
    private void ReserveStepTransition(Type source)
    {
        if (!_stepsWithTransition.Add(source))
        {
            throw new FlowDefinitionException($"Step '{source.Name}' has more than one transition defined.");
        }
    }

    /// <summary>Records a step's compiled transition (GoTo / Branch / Terminal).</summary>
    internal void AddStepTransition(Type source, CompiledTransition transition)
    {
        ReserveStepTransition(source);
        _transitions[source] = transition;
    }

    /// <summary>Records a step's transition into a gate (GoToGate): the step parks the request at the gate.</summary>
    internal void AddGateTarget(Type source, string gateName)
    {
        ReserveStepTransition(source);
        _transitions[source] = (completed, _, _) => new GateStep(completed.RequestId, gateName);
        AddEdge(source, gateName);
    }

    /// <summary>Records a gate's compiled resume routing (FromGate).</summary>
    internal void AddGateResume(string gateName, Type signalType, GateResume resume)
    {
        if (!_gateResumes.TryAdd(gateName, resume))
        {
            throw new FlowDefinitionException($"Gate '{gateName}' has more than one transition defined.");
        }

        _gateResumeSignalTypes[gateName] = signalType;
    }

    /// <summary>Records a graph edge between two nodes (step <see cref="Type"/> or gate name).</summary>
    internal void AddEdge(object source, object target)
    {
        if (!_edges.TryGetValue(source, out var targets))
        {
            targets = [];
            _edges[source] = targets;
        }

        targets.Add(target);
    }

    /// <summary>
    /// Validates the accumulated definition and produces the compiled flow.
    /// Throws <see cref="FlowDefinitionException"/> on the first structural problem found.
    /// </summary>
    public CompiledFlow Build()
    {
        if (string.IsNullOrWhiteSpace(_flowName))
        {
            throw new FlowDefinitionException("Flow name is required; call Named(\"...\").");
        }

        if (_declaredSteps.Count == 0)
        {
            throw new FlowDefinitionException($"Flow '{_flowName}' declares no steps.");
        }

        var undeclaredSources = _stepsWithTransition.Where(t => !_declaredSteps.Contains(t)).ToList();
        if (undeclaredSources.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': transition declared from undeclared step(s): {Names(undeclaredSources)}.");
        }

        var stepsWithoutTransition = _declaredSteps.Where(t => !_stepsWithTransition.Contains(t)).ToList();
        if (stepsWithoutTransition.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': step(s) without a transition: {Names(stepsWithoutTransition)}. Declare one with From<{stepsWithoutTransition[0].Name}>().");
        }

        var gatesWithoutResume = _gateSignalTypes.Keys.Where(g => !_gateResumes.ContainsKey(g)).ToList();
        if (gatesWithoutResume.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': gate(s) without a transition: {string.Join(", ", gatesWithoutResume)}. Declare one with FromGate<TSignal>(\"{gatesWithoutResume[0]}\").");
        }

        var resumesForUnknownGate = _gateResumes.Keys.Where(g => !_gateSignalTypes.ContainsKey(g)).ToList();
        if (resumesForUnknownGate.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': transition declared from undeclared gate(s): {string.Join(", ", resumesForUnknownGate)}.");
        }

        var mismatchedGate = _gateSignalTypes
            .Where(g => _gateResumeSignalTypes[g.Key] != g.Value)
            .Select(g => g.Key)
            .ToList();
        if (mismatchedGate.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': gate(s) declared and resumed with different signal types: {string.Join(", ", mismatchedGate)}.");
        }

        var allTargets = _edges.Values.SelectMany(targets => targets).ToHashSet();

        var unknownStepTargets = allTargets.OfType<Type>().Where(t => !_declaredSteps.Contains(t)).ToList();
        if (unknownStepTargets.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': transition target(s) not declared as steps: {Names(unknownStepTargets)}.");
        }

        var unknownGateTargets = allTargets.OfType<string>().Where(g => !_gateSignalTypes.ContainsKey(g)).ToList();
        if (unknownGateTargets.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': transition target(s) not declared as gates: {string.Join(", ", unknownGateTargets)}.");
        }

        var entries = _declaredSteps.Where(t => !allTargets.Contains(t)).ToList();
        if (entries.Count == 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': no entry step — every step is a transition target, so the flow has no start.");
        }

        if (entries.Count > 1)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': multiple entry steps ({Names(entries)}); a flow must have exactly one start.");
        }

        var entry = entries[0];

        var reachable = Reachable(entry);
        var unreachableNames = _declaredSteps.Where(t => !reachable.Contains(t)).Select(t => t.Name)
            .Concat(_gateSignalTypes.Keys.Where(g => !reachable.Contains(g)))
            .ToList();
        if (unreachableNames.Count > 0)
        {
            throw new FlowDefinitionException(
                $"Flow '{_flowName}': node(s) unreachable from entry '{entry.Name}': {string.Join(", ", unreachableNames)}.");
        }

        var gates = _gateSignalTypes.ToDictionary(
            g => g.Key,
            g => new GateDescriptor(g.Value, _gateResumes[g.Key]));

        return new CompiledFlow(_flowName, entry, _handlerRegistrations, _transitions, gates);
    }

    private HashSet<object> Reachable(Type entry)
    {
        var visited = new HashSet<object> { entry };
        var stack = new Stack<object>();
        stack.Push(entry);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!_edges.TryGetValue(current, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (visited.Add(target))
                {
                    stack.Push(target);
                }
            }
        }

        return visited;
    }

    private static string Names(IEnumerable<Type> types) => string.Join(", ", types.Select(t => t.Name));
}

/// <summary>Fluent transition declarations leaving one state. Created by <see cref="FlowBuilder.From{TReq}"/>.</summary>
internal sealed class TransitionSource<TReq> : ITransitionSource<TReq> where TReq : StepRequest
{
    private readonly FlowBuilder _builder;

    public TransitionSource(FlowBuilder builder) => _builder = builder;

    public IFlowBuilder GoTo<TNext>(Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(typeof(TReq), typeof(TNext));
        _builder.AddStepTransition(
            typeof(TReq),
            (completed, result, snapshot) => next((TReq)completed, result ?? new StepResult(null), snapshot));
        return _builder;
    }

    public IFlowBuilder Branch(Action<IBranch<TReq>> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var branch = new Branch<TReq>(_builder);
        cases(branch);
        _builder.AddStepTransition(typeof(TReq), branch.Compile());
        return _builder;
    }

    public IFlowBuilder GoToGate(string gateName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gateName);
        _builder.AddGateTarget(typeof(TReq), gateName);
        return _builder;
    }

    public IFlowBuilder Terminal()
    {
        _builder.AddStepTransition(typeof(TReq), static (_, _, _) => null);
        return _builder;
    }
}

/// <summary>Ordered conditional transitions out of one state. Created by <see cref="TransitionSource{TReq}.Branch"/>.</summary>
internal sealed class Branch<TReq> : IBranch<TReq> where TReq : StepRequest
{
    private readonly FlowBuilder _builder;
    private readonly List<(Func<TReq, StepResult, bool> Predicate, CompiledTransition Next)> _cases = [];
    private CompiledTransition? _otherwise;

    public Branch(FlowBuilder builder) => _builder = builder;

    public IBranch<TReq> When<TNext>(
        Func<TReq, StepResult, bool> predicate,
        Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(typeof(TReq), typeof(TNext));
        _cases.Add((
            predicate,
            (completed, result, snapshot) => next((TReq)completed, result ?? new StepResult(null), snapshot)));
        return this;
    }

    public void Otherwise<TNext>(Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(typeof(TReq), typeof(TNext));
        _otherwise = (completed, result, snapshot) => next((TReq)completed, result ?? new StepResult(null), snapshot);
    }

    public void OtherwiseTerminal() => _otherwise = static (_, _, _) => null;

    /// <summary>Compiles the ordered cases into a single transition. Requires a fallback.</summary>
    public CompiledTransition Compile()
    {
        if (_otherwise is null)
        {
            throw new FlowDefinitionException(
                $"Branch from '{typeof(TReq).Name}' must declare Otherwise(...) or OtherwiseTerminal().");
        }

        var cases = _cases.ToArray();
        var otherwise = _otherwise;

        return (completed, result, snapshot) =>
        {
            var request = (TReq)completed;
            var stepResult = result ?? new StepResult(null);

            foreach (var (predicate, next) in cases)
            {
                if (predicate(request, stepResult))
                {
                    return next(completed, result, snapshot);
                }
            }

            return otherwise(completed, result, snapshot);
        };
    }
}

/// <summary>Fluent transition declarations leaving one gate. Created by <see cref="FlowBuilder.FromGate{TSignal}"/>.</summary>
internal sealed class GateTransitions<TSignal> : IGateTransitions<TSignal> where TSignal : class
{
    private readonly FlowBuilder _builder;
    private readonly string _gateName;

    public GateTransitions(FlowBuilder builder, string gateName)
    {
        _builder = builder;
        _gateName = gateName;
    }

    public IFlowBuilder GoTo<TNext>(Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(_gateName, typeof(TNext));
        _builder.AddGateResume(_gateName, typeof(TSignal), (signal, snapshot) => next((TSignal)signal, snapshot));
        return _builder;
    }

    public IFlowBuilder Branch(Action<IGateBranch<TSignal>> cases)
    {
        ArgumentNullException.ThrowIfNull(cases);
        var branch = new GateBranch<TSignal>(_builder, _gateName);
        cases(branch);
        _builder.AddGateResume(_gateName, typeof(TSignal), branch.Compile());
        return _builder;
    }
}

/// <summary>Ordered conditional transitions out of one gate. Created by <see cref="GateTransitions{TSignal}.Branch"/>.</summary>
internal sealed class GateBranch<TSignal> : IGateBranch<TSignal> where TSignal : class
{
    private readonly FlowBuilder _builder;
    private readonly string _gateName;
    private readonly List<(Func<TSignal, bool> Predicate, GateResume Next)> _cases = [];
    private GateResume? _otherwise;

    public GateBranch(FlowBuilder builder, string gateName)
    {
        _builder = builder;
        _gateName = gateName;
    }

    public IGateBranch<TSignal> When<TNext>(
        Func<TSignal, bool> predicate,
        Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(_gateName, typeof(TNext));
        _cases.Add((predicate, (signal, snapshot) => next((TSignal)signal, snapshot)));
        return this;
    }

    public void Otherwise<TNext>(Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest
    {
        ArgumentNullException.ThrowIfNull(next);
        _builder.AddEdge(_gateName, typeof(TNext));
        _otherwise = (signal, snapshot) => next((TSignal)signal, snapshot);
    }

    public void OtherwiseTerminal() => _otherwise = static (_, _) => null;

    /// <summary>Compiles the ordered cases into a single gate resume. Requires a fallback.</summary>
    public GateResume Compile()
    {
        if (_otherwise is null)
        {
            throw new FlowDefinitionException(
                $"Branch from gate '{_gateName}' must declare Otherwise(...) or OtherwiseTerminal().");
        }

        var cases = _cases.ToArray();
        var otherwise = _otherwise;

        return (signal, snapshot) =>
        {
            var typed = (TSignal)signal;

            foreach (var (predicate, next) in cases)
            {
                if (predicate(typed))
                {
                    return next(signal, snapshot);
                }
            }

            return otherwise(signal, snapshot);
        };
    }
}