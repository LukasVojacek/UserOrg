namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Resolves registered <see cref="IRequestStrategy"/> implementations by name.
/// </summary>
public interface IStrategyRegistry
{
    /// <summary>
    /// Returns the strategy registered under <paramref name="strategyName"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no strategy with the given name has been registered.
    /// </exception>
    IRequestStrategy Resolve(string strategyName);

    /// <summary>
    /// Returns the strategy whose <see cref="IRequestStrategy.InitialStepType"/> matches
    /// <paramref name="initialStepType"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no strategy handles the given initial step type.
    /// </exception>
    IRequestStrategy ResolveByStepType(Type initialStepType);
}