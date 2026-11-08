using Microsoft.Extensions.DependencyInjection;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.Flows;

namespace WorkflowEngine.Core.DependencyInjection;

/// <summary>
/// Registration entry point for flows defined with the fluent builder DSL.
/// </summary>
public static class FlowRegistration
{
    /// <summary>
    /// Compiles the <typeparamref name="TFlow"/> definition, validates its graph, and registers everything
    /// the engine needs: one handler per declared step and a synthesized <see cref="IRequestStrategy"/>.
    /// Call after <c>AddWorkflowEngine</c>.
    /// </summary>
    /// <exception cref="FlowDefinitionException">Thrown when the flow definition is structurally invalid.</exception>
    public static IServiceCollection AddFlow<TFlow>(this IServiceCollection services)
        where TFlow : class, IFlowDefinition, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var definition = new TFlow();
        var builder = new FlowBuilder();
        definition.Define(builder);
        var compiled = builder.Build();

        foreach (var register in compiled.HandlerRegistrations)
        {
            register(services);
        }

        services.AddSingleton<IRequestStrategy>(
            new CompiledFlowStrategy(compiled.Name, compiled.EntryType, compiled.Transitions, compiled.Gates));

        // Expose the definition so IWorkflowClient.StartAsync<TFlow> can build the initial step.
        services.AddSingleton<IFlowDefinition>(definition);

        return services;
    }
}