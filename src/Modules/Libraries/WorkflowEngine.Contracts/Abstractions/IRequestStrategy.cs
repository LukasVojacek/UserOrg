namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Defines the complete flow for a single workflow strategy as a typed state machine.
/// A strategy owns what domain states exist, what request type carries the work for each state,
/// and what transitions are valid.
/// </summary>
/// <example>
/// <code>
/// services.AddSingleton&lt;IRequestStrategy, HEAppEStrategy&gt;();
/// </code>
/// </example>
public interface IRequestStrategy
{
    /// <summary>Unique name of this strategy (e.g. <c>"heappe-provision"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// The concrete <see cref="StepRequest"/> subtype that represents the first step of this strategy.
    /// Used by the engine to route a directly-submitted <see cref="StepRequest"/> to the correct strategy
    /// without requiring the caller to know the strategy name.
    /// </summary>
    Type InitialStepType { get; }

    /// <summary>
    /// Pure, side-effect-free state transition function.
    /// Given the completed step message and its result, returns the next typed step message
    /// or <c>null</c> to signal a terminal (completed) state.
    /// </summary>
    /// <param name="completedStep">The step message that was just successfully executed.</param>
    /// <param name="stepResult">Result produced by the handler; may be <c>null</c> when no data was produced.</param>
    /// <param name="request">Read-only snapshot of the current request.</param>
    /// <returns>Next typed message to enqueue, or <c>null</c> when the flow is finished.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="completedStep"/> type is not a recognized step — this is a programming error.
    /// </exception>
    StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request);
}