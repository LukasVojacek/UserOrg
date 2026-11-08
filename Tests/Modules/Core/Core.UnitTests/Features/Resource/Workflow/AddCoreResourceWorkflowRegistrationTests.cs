using System.Linq;

using Core.Features.Resource.Workflow;
using Core.Features.Resource.Workflow.Steps;

using Microsoft.Extensions.DependencyInjection;

using WorkflowEngine.Contracts.Abstractions;

using Xunit;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// Composition tests for <see cref="ServiceCollectionExtensions.AddCoreResourceWorkflow"/>.
/// Verifies every workflow step in the create-resource pipeline has its handler registered,
/// which is the precondition for the WorkflowEngine worker being able to dispatch each step.
/// </summary>
public sealed class AddCoreResourceWorkflowRegistrationTests
{
    private static bool IsHandlerRegistered<TStep>(IServiceCollection services)
        where TStep : StepRequest =>
        services.Any(d => d.ServiceType == typeof(IStepHandler<TStep>));

    private static IServiceCollection ComposeWorkflow() =>
        new ServiceCollection().AddCoreResourceWorkflow();

    [Fact]
    public void Should_RegisterCheckHeappeHandler_When_WorkflowComposed()
    {
        var services = ComposeWorkflow();

        Assert.True(IsHandlerRegistered<CheckHeappeStep>(services));
    }

    [Fact]
    public void Should_RegisterInitializeProjectResourceHandler_When_WorkflowComposed()
    {
        var services = ComposeWorkflow();

        Assert.True(IsHandlerRegistered<InitializeProjectResourceStep>(services));
    }

    [Fact]
    public void Should_RegisterInitializeHeappeHandler_When_WorkflowComposed()
    {
        var services = ComposeWorkflow();

        Assert.True(IsHandlerRegistered<InitializeHeappeStep>(services));
    }

    [Fact]
    public void Should_RegisterFinalizeCreationProjectResourceHandler_When_WorkflowComposed()
    {
        var services = ComposeWorkflow();

        Assert.True(IsHandlerRegistered<FinalizeCreationProjectResourceStep>(services));
    }

    [Fact]
    public void Should_RegisterSendEmailHandler_When_WorkflowComposed()
    {
        var services = ComposeWorkflow();

        Assert.True(IsHandlerRegistered<SendEmailStep>(services));
    }
}
