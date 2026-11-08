using NSubstitute;
using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.Application;
using WorkflowEngine.Core.Tests.Helpers;

namespace WorkflowEngine.Core.Tests.Application;

// Distinct step types needed for multi-strategy tests so each strategy maps to a unique InitialStepType.
file sealed record StepTypeA(Guid RequestId) : StepRequest(RequestId, "a");
file sealed record StepTypeB(Guid RequestId) : StepRequest(RequestId, "b");
file sealed record StepTypeC(Guid RequestId) : StepRequest(RequestId, "c");

public sealed class StrategyRegistryImplTests
{
    private static IRequestStrategy MakeStrategy(string name, Type initialStepType)
    {
        var s = Substitute.For<IRequestStrategy>();
        s.Name.Returns(name);
        s.InitialStepType.Returns(initialStepType);
        return s;
    }

    [Fact(DisplayName = "Constructor throws ArgumentNullException when strategies collection is null")]
    public void Should_ThrowArgumentNullException_When_StrategiesIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => new StrategyRegistryImpl(null!));
    }

    [Fact(DisplayName = "Resolve returns the registered strategy for an exact-match name")]
    public void Should_ReturnStrategy_When_ExactNameProvided()
    {
        var strategy = MakeStrategy("heappe-flow", typeof(TestStepRequest));
        var registry = new StrategyRegistryImpl([strategy]);

        var result = registry.Resolve("heappe-flow");

        Assert.Same(strategy, result);
    }

    [Fact(DisplayName = "Resolve is case-insensitive")]
    public void Should_ReturnStrategy_When_NameCaseDiffers()
    {
        var strategy = MakeStrategy("MyFlow", typeof(TestStepRequest));
        var registry = new StrategyRegistryImpl([strategy]);

        var result = registry.Resolve("myflow");

        Assert.Same(strategy, result);
    }

    [Fact(DisplayName = "Resolve throws ArgumentNullException when strategyName is null")]
    public void Should_ThrowArgumentNullException_When_StrategyNameIsNull()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("any", typeof(TestStepRequest))]);

        Assert.ThrowsAny<ArgumentException>(() => registry.Resolve(null!));
    }

    [Fact(DisplayName = "Resolve throws ArgumentException when strategyName is whitespace")]
    public void Should_ThrowArgumentException_When_StrategyNameIsWhitespace()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("any", typeof(TestStepRequest))]);

        Assert.Throws<ArgumentException>(() => registry.Resolve("   "));
    }

    [Fact(DisplayName = "Resolve throws InvalidOperationException when strategy is not registered")]
    public void Should_ThrowInvalidOperationException_When_StrategyNotRegistered()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("known", typeof(TestStepRequest))]);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("unknown"));

        Assert.Contains("unknown", ex.Message);
        Assert.Contains("known", ex.Message);
    }

    [Fact(DisplayName = "Resolve lists all registered strategy names in the exception message")]
    public void Should_ListRegisteredNamesInMessage_When_StrategyNotFound()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("flow-a", typeof(StepTypeA)), MakeStrategy("flow-b", typeof(StepTypeB))]);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.Resolve("flow-c"));

        Assert.Contains("flow-a", ex.Message);
        Assert.Contains("flow-b", ex.Message);
    }

    [Fact(DisplayName = "Resolve returns correct strategy when multiple strategies are registered")]
    public void Should_ReturnCorrectStrategy_When_MultipleStrategiesRegistered()
    {
        var stratA = MakeStrategy("flow-a", typeof(StepTypeA));
        var stratB = MakeStrategy("flow-b", typeof(StepTypeB));
        var registry = new StrategyRegistryImpl([stratA, stratB]);

        Assert.Same(stratA, registry.Resolve("flow-a"));
        Assert.Same(stratB, registry.Resolve("flow-b"));
    }

    [Fact(DisplayName = "Constructor throws when two strategies share the same name")]
    public void Should_ThrowArgumentException_When_DuplicateStrategyNamesRegistered()
    {
        var s1 = MakeStrategy("dupe", typeof(StepTypeA));
        var s2 = MakeStrategy("dupe", typeof(StepTypeB));

        Assert.Throws<ArgumentException>(() => new StrategyRegistryImpl([s1, s2]));
    }

    [Fact(DisplayName = "Empty strategies collection can be built; Resolve always throws")]
    public void Should_AlwaysThrow_When_StrategiesCollectionIsEmpty()
    {
        var registry = new StrategyRegistryImpl([]);

        Assert.Throws<InvalidOperationException>(() => registry.Resolve("anything"));
    }

    [Fact(DisplayName = "ResolveByStepType returns the strategy for the registered initial step type")]
    public void Should_ReturnStrategy_When_StepTypeMatches()
    {
        var strategy = MakeStrategy("heappe-flow", typeof(TestStepRequest));
        var registry = new StrategyRegistryImpl([strategy]);

        var result = registry.ResolveByStepType(typeof(TestStepRequest));

        Assert.Same(strategy, result);
    }

    [Fact(DisplayName = "ResolveByStepType throws InvalidOperationException when step type not registered")]
    public void Should_ThrowInvalidOperationException_When_StepTypeNotRegistered()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("flow-a", typeof(StepTypeA))]);

        var ex = Assert.Throws<InvalidOperationException>(() => registry.ResolveByStepType(typeof(StepTypeB)));

        Assert.Contains(nameof(StepTypeB), ex.Message);
        Assert.Contains(nameof(StepTypeA), ex.Message);
    }

    [Fact(DisplayName = "ResolveByStepType throws ArgumentNullException when step type is null")]
    public void Should_ThrowArgumentNullException_When_StepTypeIsNull()
    {
        var registry = new StrategyRegistryImpl([MakeStrategy("any", typeof(TestStepRequest))]);

        Assert.Throws<ArgumentNullException>(() => registry.ResolveByStepType(null!));
    }

    [Fact(DisplayName = "ResolveByStepType returns correct strategy when multiple strategies are registered")]
    public void Should_ReturnCorrectStrategy_ByStepType_When_MultipleStrategiesRegistered()
    {
        var stratA = MakeStrategy("flow-a", typeof(StepTypeA));
        var stratB = MakeStrategy("flow-b", typeof(StepTypeB));
        var registry = new StrategyRegistryImpl([stratA, stratB]);

        Assert.Same(stratA, registry.ResolveByStepType(typeof(StepTypeA)));
        Assert.Same(stratB, registry.ResolveByStepType(typeof(StepTypeB)));
    }
}
