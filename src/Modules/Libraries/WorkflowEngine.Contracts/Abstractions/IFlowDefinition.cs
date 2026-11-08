namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Declarative definition of one workflow flow: its states, the handler bound to each state,
/// and the transitions between them. Authored by a library consumer and registered with
/// <c>AddFlow&lt;TFlow&gt;()</c>, which compiles it into an <see cref="IRequestStrategy"/> and validates
/// the graph at startup.
/// </summary>
public interface IFlowDefinition
{
    /// <summary>
    /// Declares the flow's states, handlers, and transitions onto the supplied <paramref name="flow"/> builder.
    /// Called once at startup.
    /// </summary>
    void Define(IFlowBuilder flow);

    /// <summary>
    /// Builds the first step message of a new request from caller-supplied input.
    /// Lets the flow own initial-step construction so callers start a request with their own input type
    /// rather than a pre-built <see cref="StepRequest"/>.
    /// </summary>
    /// <param name="requestId">The identifier assigned to the new request.</param>
    /// <param name="input">Caller-supplied input; the flow casts it to its expected input type.</param>
    StepRequest CreateInitialStep(Guid requestId, object input);
}