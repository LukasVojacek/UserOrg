namespace WorkflowEngine.Contracts.Abstractions;

/// <summary>
/// Fluent surface for declaring a flow's states and transitions. Obtained inside
/// <see cref="IFlowDefinition.Define"/>. State declarations are order-independent; the entry state is
/// derived as the single declared step that is never a transition target.
/// </summary>
/// <remarks>
/// <para><b>Routing must be pure.</b> The transition delegates passed to <see cref="ITransitionSource{TReq}"/>
/// and <see cref="IBranch{TReq}"/> are re-run during crash-recovery replay. They may read the completed
/// step, its result, and the read-only <see cref="IRequestSnapshot"/> — and nothing else. They must not
/// perform I/O or read mutable state. Purity is a contract the compiler cannot enforce.</para>
/// </remarks>
public interface IFlowBuilder
{
    /// <summary>Sets the flow's unique name (used as the strategy name). Required.</summary>
    IFlowBuilder Named(string flowName);

    /// <summary>Declares a state and binds it to its handler. Order-independent.</summary>
    /// <typeparam name="TReq">The step message type for this state.</typeparam>
    /// <typeparam name="THandler">The handler that processes <typeparamref name="TReq"/>.</typeparam>
    IFlowBuilder Step<TReq, THandler>()
        where TReq : StepRequest
        where THandler : class, IStepHandler<TReq>;

    /// <summary>
    /// Declares a gate: a handler-less state that parks the request awaiting a <typeparamref name="TSignal"/>.
    /// Order-independent. Wire the gate's outgoing transition with <see cref="FromGate{TSignal}"/>.
    /// </summary>
    /// <typeparam name="TSignal">The signal type that resumes the gate.</typeparam>
    /// <param name="gateName">Unique gate name; also the current state name while parked.</param>
    IFlowBuilder Gate<TSignal>(string gateName) where TSignal : class;

    /// <summary>Begins declaring the transition out of the <typeparamref name="TReq"/> state.</summary>
    ITransitionSource<TReq> From<TReq>() where TReq : StepRequest;

    /// <summary>Begins declaring the transition out of the named gate, driven by its signal.</summary>
    /// <typeparam name="TSignal">The gate's signal type (must match its <see cref="Gate{TSignal}"/> declaration).</typeparam>
    IGateTransitions<TSignal> FromGate<TSignal>(string gateName) where TSignal : class;
}

/// <summary>
/// Declares the single transition leaving one state.
/// </summary>
/// <typeparam name="TReq">The state whose outgoing transition is being declared.</typeparam>
public interface ITransitionSource<TReq> where TReq : StepRequest
{
    /// <summary>
    /// Routes unconditionally to the next state. The delegate (re-run on replay; must be pure) reads the
    /// completed step, its result, and the read-only snapshot to build the next step message.
    /// </summary>
    IFlowBuilder GoTo<TNext>(Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Routes to one of several next states based on ordered predicates.</summary>
    IFlowBuilder Branch(Action<IBranch<TReq>> cases);

    /// <summary>Routes into a gate: completing this state parks the request awaiting the gate's signal.</summary>
    IFlowBuilder GoToGate(string gateName);

    /// <summary>Marks this state as terminal: completing it completes the request.</summary>
    IFlowBuilder Terminal();
}

/// <summary>
/// Ordered set of conditional transitions out of one state. The first matching <see cref="When{TNext}"/>
/// wins; a fallback (<see cref="Otherwise{TNext}"/> or <see cref="OtherwiseTerminal"/>) is mandatory.
/// </summary>
/// <typeparam name="TReq">The state whose branch is being declared.</typeparam>
public interface IBranch<TReq> where TReq : StepRequest
{
    /// <summary>Adds a conditional case. Predicates are pure and evaluated in declaration order.</summary>
    IBranch<TReq> When<TNext>(
        Func<TReq, StepResult, bool> predicate,
        Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Fallback route taken when no <see cref="When{TNext}"/> predicate matched.</summary>
    void Otherwise<TNext>(Func<TReq, StepResult, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Fallback that terminates the request when no predicate matched.</summary>
    void OtherwiseTerminal();
}

/// <summary>
/// Declares the single transition leaving a gate, driven by the delivered signal.
/// </summary>
/// <typeparam name="TSignal">The gate's signal type.</typeparam>
public interface IGateTransitions<TSignal> where TSignal : class
{
    /// <summary>
    /// Routes unconditionally to the next state on resume. The delegate (pure; reads the signal and the
    /// read-only snapshot) builds the next step message.
    /// </summary>
    IFlowBuilder GoTo<TNext>(Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Routes to one of several next states based on ordered predicates over the signal.</summary>
    IFlowBuilder Branch(Action<IGateBranch<TSignal>> cases);
}

/// <summary>
/// Ordered set of conditional transitions leaving a gate. The first matching <see cref="When{TNext}"/>
/// wins; a fallback (<see cref="Otherwise{TNext}"/> or <see cref="OtherwiseTerminal"/>) is mandatory.
/// </summary>
/// <typeparam name="TSignal">The gate's signal type.</typeparam>
public interface IGateBranch<TSignal> where TSignal : class
{
    /// <summary>Adds a conditional case over the signal. Predicates are pure and evaluated in order.</summary>
    IGateBranch<TSignal> When<TNext>(
        Func<TSignal, bool> predicate,
        Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Fallback route taken when no <see cref="When{TNext}"/> predicate matched.</summary>
    void Otherwise<TNext>(Func<TSignal, IRequestSnapshot, TNext> next) where TNext : StepRequest;

    /// <summary>Fallback that terminates the request when no predicate matched.</summary>
    void OtherwiseTerminal();
}