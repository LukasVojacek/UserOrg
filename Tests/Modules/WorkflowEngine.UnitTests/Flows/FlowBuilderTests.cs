using Microsoft.Extensions.DependencyInjection;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Flows;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Flows;

public sealed class FlowBuilderTests
{

    private sealed record StepA(Guid RequestId) : StepRequest(RequestId, nameof(StepA));
    private sealed record StepB(Guid RequestId, bool Accept) : StepRequest(RequestId, nameof(StepB));
    private sealed record StepC(Guid RequestId) : StepRequest(RequestId, nameof(StepC));
    private sealed record StepD(Guid RequestId) : StepRequest(RequestId, nameof(StepD));

    private sealed class NoopHandler<T> : IStepHandler<T> where T : StepRequest
    {
        public Task<StepResult> HandleAsync(T request, CancellationToken ct) => Task.FromResult(new StepResult(null));
    }

    // ── A valid flow: A -> B -> (Accept ? C : terminal); C terminal ─────────────────

    private sealed class HappyFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("happy")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Step<StepC, NoopHandler<StepC>>()
            .From<StepA>().GoTo((a, r, snap) => new StepB(a.RequestId, true))
            .From<StepB>().Branch(c => c
                .When((b, r) => b.Accept, (b, r, snap) => new StepC(b.RequestId))
                .OtherwiseTerminal())
            .From<StepC>().Terminal();
    }

    private static IRequestStrategy ResolveStrategy<TFlow>(string name) where TFlow : class, IFlowDefinition, new()
    {
        var services = new ServiceCollection();
        services.AddFlow<TFlow>();
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IRequestStrategy>().Single(s => s.Name == name);
    }

    [Fact(DisplayName = "AddFlow derives the entry step as the one that is never a target")]
    public void Should_DeriveEntryStep_When_FlowCompiled()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");

        Assert.Equal(typeof(StepA), strategy.InitialStepType);
    }

    [Fact(DisplayName = "Compiled strategy routes a GoTo transition to the next step")]
    public void Should_RouteGoTo_When_TransitionCalled()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");
        var id = Guid.NewGuid();

        var next = strategy.Transition(new StepA(id), new StepResult(null), RequestAggregateBuilder.Build());

        var stepB = Assert.IsType<StepB>(next);
        Assert.Equal(id, stepB.RequestId);
    }

    [Fact(DisplayName = "Compiled strategy takes the matching branch case")]
    public void Should_RouteMatchingBranchCase_When_PredicateTrue()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");
        var id = Guid.NewGuid();

        var next = strategy.Transition(new StepB(id, Accept: true), new StepResult(null), RequestAggregateBuilder.Build());

        Assert.IsType<StepC>(next);
    }

    [Fact(DisplayName = "Compiled strategy takes the branch fallback when no case matches")]
    public void Should_RouteBranchFallback_When_NoPredicateMatches()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");
        var id = Guid.NewGuid();

        var next = strategy.Transition(new StepB(id, Accept: false), new StepResult(null), RequestAggregateBuilder.Build());

        Assert.Null(next);
    }

    [Fact(DisplayName = "Compiled strategy returns null for a terminal step")]
    public void Should_ReturnNull_When_TerminalStepCompletes()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");

        var next = strategy.Transition(new StepC(Guid.NewGuid()), new StepResult(null), RequestAggregateBuilder.Build());

        Assert.Null(next);
    }

    [Fact(DisplayName = "Compiled strategy throws for a step it has no transition for")]
    public void Should_Throw_When_TransitionCalledForUnknownStep()
    {
        var strategy = ResolveStrategy<HappyFlow>("happy");

        Assert.Throws<InvalidOperationException>(
            () => strategy.Transition(new StepD(Guid.NewGuid()), new StepResult(null), RequestAggregateBuilder.Build()));
    }

    [Fact(DisplayName = "AddFlow registers a handler for every declared step")]
    public void Should_RegisterHandlerPerStep_When_FlowAdded()
    {
        var services = new ServiceCollection();
        services.AddFlow<HappyFlow>();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IStepHandler<StepA>>());
        Assert.NotNull(provider.GetService<IStepHandler<StepB>>());
        Assert.NotNull(provider.GetService<IStepHandler<StepC>>());
    }

    private sealed class NoNameFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Step<StepA, NoopHandler<StepA>>()
            .From<StepA>().Terminal();
    }

    private sealed class MissingTransitionFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("missing-transition")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .From<StepA>().GoTo((a, r, snap) => new StepB(a.RequestId, true));
    }

    private sealed class UnknownTargetFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("unknown-target")
            .Step<StepA, NoopHandler<StepA>>()
            .From<StepA>().GoTo((a, r, snap) => new StepB(a.RequestId, true));
    }

    private sealed class MultipleEntryFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("multiple-entry")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .From<StepA>().Terminal()
            .From<StepB>().Terminal();
    }

    private sealed class UnreachableFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("unreachable")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Step<StepC, NoopHandler<StepC>>()
            .Step<StepD, NoopHandler<StepD>>()
            .From<StepA>().GoTo((a, r, snap) => new StepB(a.RequestId, true))
            .From<StepB>().Terminal()
            .From<StepC>().GoTo((c, r, snap) => new StepD(c.RequestId))
            .From<StepD>().GoTo((d, r, snap) => new StepC(d.RequestId));
    }

    private sealed class BranchWithoutFallbackFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("branch-no-fallback")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .From<StepA>().GoTo((a, r, snap) => new StepB(a.RequestId, true))
            .From<StepB>().Branch(c => c
                .When((b, r) => b.Accept, (b, r, snap) => new StepA(b.RequestId)));
    }

    private sealed class DuplicateStepFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("duplicate-step")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepA, NoopHandler<StepA>>()
            .From<StepA>().Terminal();
    }

    private static void AssertInvalid<TFlow>() where TFlow : class, IFlowDefinition, new()
        => Assert.Throws<FlowDefinitionException>(() => new ServiceCollection().AddFlow<TFlow>());

    [Fact(DisplayName = "AddFlow throws when the flow has no name")]
    public void Should_Throw_When_NameMissing() => AssertInvalid<NoNameFlow>();

    [Fact(DisplayName = "AddFlow throws when a declared step has no transition")]
    public void Should_Throw_When_StepHasNoTransition() => AssertInvalid<MissingTransitionFlow>();

    [Fact(DisplayName = "AddFlow throws when a transition targets an undeclared step")]
    public void Should_Throw_When_TargetNotDeclared() => AssertInvalid<UnknownTargetFlow>();

    [Fact(DisplayName = "AddFlow throws when there is more than one entry step")]
    public void Should_Throw_When_MultipleEntries() => AssertInvalid<MultipleEntryFlow>();

    [Fact(DisplayName = "AddFlow throws when a step is unreachable from the entry")]
    public void Should_Throw_When_StepUnreachable() => AssertInvalid<UnreachableFlow>();

    [Fact(DisplayName = "AddFlow throws when a branch has no fallback")]
    public void Should_Throw_When_BranchHasNoFallback() => AssertInvalid<BranchWithoutFallbackFlow>();

    [Fact(DisplayName = "AddFlow throws when a step is declared twice")]
    public void Should_Throw_When_StepDeclaredTwice() => AssertInvalid<DuplicateStepFlow>();

    private sealed record Approval(bool Accept);
    private sealed record OtherSignal(int Value);

    // A -> Gate(ApprovalGate) -> (Accept ? B : terminal); B terminal
    private sealed class GatedFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);

        public void Define(IFlowBuilder flow) => flow
            .Named("gated")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("ApprovalGate")
            .From<StepA>().GoToGate("ApprovalGate")
            .FromGate<Approval>("ApprovalGate").Branch(c => c
                .When(a => a.Accept, (a, snap) => new StepB(snap.RequestId, true))
                .OtherwiseTerminal())
            .From<StepB>().Terminal();
    }

    [Fact(DisplayName = "A step that routes into a gate yields a GateStep sentinel")]
    public void Should_YieldGateStep_When_StepRoutesToGate()
    {
        var strategy = ResolveStrategy<GatedFlow>("gated");

        var next = strategy.Transition(new StepA(Guid.NewGuid()), new StepResult(null), RequestAggregateBuilder.Build());

        var gate = Assert.IsType<GateStep>(next);
        Assert.Equal("ApprovalGate", gate.GateName);
    }

    [Fact(DisplayName = "Gate descriptor exposes the declared signal type")]
    public void Should_ExposeSignalType_When_GateCompiled()
    {
        var gated = Assert.IsAssignableFrom<IGatedStrategy>(ResolveStrategy<GatedFlow>("gated"));

        Assert.True(gated.TryGetGate("ApprovalGate", out var descriptor));
        Assert.Equal(typeof(Approval), descriptor.SignalType);
    }

    [Fact(DisplayName = "Gate resume routes the matching branch case for the delivered signal")]
    public void Should_RouteResume_When_SignalDelivered()
    {
        var gated = Assert.IsAssignableFrom<IGatedStrategy>(ResolveStrategy<GatedFlow>("gated"));
        gated.TryGetGate("ApprovalGate", out var descriptor);
        var snapshot = RequestAggregateBuilder.Build(requestId: Guid.NewGuid());

        var accepted = descriptor!.Resume(new Approval(Accept: true), snapshot);
        var rejected = descriptor.Resume(new Approval(Accept: false), snapshot);

        Assert.IsType<StepB>(accepted);
        Assert.Null(rejected);
    }

    [Fact(DisplayName = "TryGetGate returns false for an unknown gate")]
    public void Should_ReturnFalse_When_GateUnknown()
    {
        var gated = Assert.IsAssignableFrom<IGatedStrategy>(ResolveStrategy<GatedFlow>("gated"));

        Assert.False(gated.TryGetGate("NoSuchGate", out _));
    }

    private sealed class GateWithoutResumeFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("gate-no-resume")
            .Step<StepA, NoopHandler<StepA>>()
            .Gate<Approval>("X")
            .From<StepA>().GoToGate("X");
    }

    private sealed class ResumeForUnknownGateFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("resume-unknown-gate")
            .Step<StepA, NoopHandler<StepA>>()
            .From<StepA>().Terminal()
            .FromGate<Approval>("Ghost").GoTo((a, snap) => new StepA(snap.RequestId));
    }

    private sealed class GateSignalMismatchFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("gate-signal-mismatch")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("X")
            .From<StepA>().GoToGate("X")
            .FromGate<OtherSignal>("X").GoTo((o, snap) => new StepB(snap.RequestId, true))
            .From<StepB>().Terminal();
    }

    private sealed class GoToUndeclaredGateFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("goto-undeclared-gate")
            .Step<StepA, NoopHandler<StepA>>()
            .From<StepA>().GoToGate("Ghost");
    }

    private sealed class GateBranchWithoutFallbackFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("gate-branch-no-fallback")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("X")
            .From<StepA>().GoToGate("X")
            .FromGate<Approval>("X").Branch(c => c
                .When(a => a.Accept, (a, snap) => new StepB(snap.RequestId, true)))
            .From<StepB>().Terminal();
    }

    private sealed class UnreachableGateFlow : IFlowDefinition
    {
        public StepRequest CreateInitialStep(Guid requestId, object input) => new StepA(requestId);
        public void Define(IFlowBuilder flow) => flow
            .Named("unreachable-gate")
            .Step<StepA, NoopHandler<StepA>>()
            .Step<StepB, NoopHandler<StepB>>()
            .Gate<Approval>("X")
            .From<StepA>().Terminal()
            .From<StepB>().GoToGate("X")
            .FromGate<Approval>("X").GoTo((a, snap) => new StepB(snap.RequestId, true));
    }

    [Fact(DisplayName = "AddFlow throws when a gate has no transition")]
    public void Should_Throw_When_GateHasNoResume() => AssertInvalid<GateWithoutResumeFlow>();

    [Fact(DisplayName = "AddFlow throws when FromGate names an undeclared gate")]
    public void Should_Throw_When_ResumeForUnknownGate() => AssertInvalid<ResumeForUnknownGateFlow>();

    [Fact(DisplayName = "AddFlow throws when a gate's declared and resumed signal types differ")]
    public void Should_Throw_When_GateSignalTypesMismatch() => AssertInvalid<GateSignalMismatchFlow>();

    [Fact(DisplayName = "AddFlow throws when GoToGate names an undeclared gate")]
    public void Should_Throw_When_GoToUndeclaredGate() => AssertInvalid<GoToUndeclaredGateFlow>();

    [Fact(DisplayName = "AddFlow throws when a gate branch has no fallback")]
    public void Should_Throw_When_GateBranchHasNoFallback() => AssertInvalid<GateBranchWithoutFallbackFlow>();

    [Fact(DisplayName = "AddFlow throws when a gate is unreachable from the entry")]
    public void Should_Throw_When_GateUnreachable() => AssertInvalid<UnreachableGateFlow>();
}
