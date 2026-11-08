using WorkflowEngine.Contracts.Abstractions;

namespace WorkflowEngine.Core.Application;

/// <summary>
/// Resolves <see cref="IRequestStrategy"/> implementations by name or initial step type
/// from the DI container.
/// </summary>
internal sealed class StrategyRegistryImpl : IStrategyRegistry
{
    private readonly IReadOnlyDictionary<string, IRequestStrategy> _byName;
    private readonly IReadOnlyDictionary<Type, IRequestStrategy> _byStepType;

    public StrategyRegistryImpl(IEnumerable<IRequestStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var list = strategies.ToList();
        _byName = list.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        _byStepType = list.ToDictionary(s => s.InitialStepType);
    }

    /// <inheritdoc/>
    public IRequestStrategy Resolve(string strategyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyName, nameof(strategyName));

        if (_byName.TryGetValue(strategyName, out var strategy))
        {
            return strategy;
        }

        throw new InvalidOperationException(
            $"No strategy registered with name '{strategyName}'. Registered: [{string.Join(", ", _byName.Keys)}].");
    }

    /// <inheritdoc/>
    public IRequestStrategy ResolveByStepType(Type initialStepType)
    {
        ArgumentNullException.ThrowIfNull(initialStepType);

        if (_byStepType.TryGetValue(initialStepType, out var strategy))
        {
            return strategy;
        }

        throw new InvalidOperationException(
            $"No strategy registered for initial step type '{initialStepType.Name}'. Registered initial step types: [{string.Join(", ", _byStepType.Keys.Select(t => t.Name))}].");
    }
}