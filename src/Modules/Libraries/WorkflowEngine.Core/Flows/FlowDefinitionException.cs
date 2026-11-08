namespace WorkflowEngine.Core.Flows;

/// <summary>
/// Thrown at startup when a flow definition is malformed — e.g. a missing name, a step without a
/// transition, a transition target that is not a declared step, zero or multiple entry steps, an
/// unreachable step, or a branch without a fallback. Surfacing these at <c>AddFlow</c> time turns a
/// would-be mid-flight failure into a fail-fast configuration error.
/// </summary>
public sealed class FlowDefinitionException : Exception
{
    /// <summary>Creates the exception with a descriptive message.</summary>
    public FlowDefinitionException(string message) : base(message) { }

    /// <summary>Creates the exception with a descriptive message and an inner cause.</summary>
    public FlowDefinitionException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>Creates the exception with no message.</summary>
    public FlowDefinitionException() { }
}