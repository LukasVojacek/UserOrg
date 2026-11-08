using Core.Features.Resource.Workflow.Handlers;
using Core.Features.Resource.Workflow.Steps;

using Microsoft.Extensions.DependencyInjection;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Core.DependencyInjection;
using WorkflowEngine.Core.Ports;

namespace Core.Features.Resource.Workflow;

internal static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Register Core.Features.Resource.Workflow services:
        /// - IRequestStrategy (public interface for workflow strategies)
        /// - IStepHandler{T} (public interface for step handlers)
        /// - ICallbackPort (platform logging adapter — RequestWorker requires one, ADR-0056 A4)
        /// </summary>
        public IServiceCollection AddCoreResourceWorkflow()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IRequestStrategy, CreateStrategy>();
            services.AddSingleton<IRequestStrategy, CreateStrategyWithoutHeappe>();

            services.AddStepHandler<CheckHeappeStep, CheckHeappeHandler>();
            services.AddStepHandler<InitializeProjectResourceStep, InitializeProjectResourceHandler>();
            services.AddStepHandler<InitializeHeappeStep, InitializeHeappeHandler>();
            services.AddStepHandler<FinalizeCreationProjectResourceStep, FinalizeCreationProjectResourceHandler>();
            services.AddStepHandler<SendEmailStep, SendEmailHandler>();

            // AddWorkflowEngine deliberately does not register an ICallbackPort, but RequestWorker (a
            // singleton hosted service) requires one. Wire the platform logging adapter here — singleton
            // so it can be constructor-injected into the worker; real transport is deferred to #239 (ADR-0056 A4).
            services.AddSingleton<ICallbackPort, LoggingCallbackAdapter>();

            return services;
        }
    }
}