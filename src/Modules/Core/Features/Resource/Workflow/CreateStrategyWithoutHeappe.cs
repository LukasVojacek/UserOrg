using System;
using System.Collections.Generic;
using System.Text;

using Core.Features.Resource.Workflow.Steps;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow;

internal sealed class CreateStrategyWithoutHeappe : IRequestStrategy
{
    public string Name => "create-project-resource-without-heappe";

    public Type InitialStepType => typeof(InitializeProjectResourceStep);

    public StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request)
    {
        if (stepResult?.hasContinue == false)
            return null;

        return completedStep switch
        {
            InitializeProjectResourceStep s => new FinalizeCreationProjectResourceStep(s.RequestId, s.ProviderName, s.Data.Name, s.Data.PrimaryInvestigatorEmail, s.Data.AssignedLocations, s.ResolvedLocations, stepResult?.Data),
            FinalizeCreationProjectResourceStep s => new SendEmailStep(s.RequestId, s.Name, s.ProviderName, stepResult?.Data),
            SendEmailStep => null,
            _ => throw new InvalidOperationException($"Unrecognized step type '{completedStep.GetType().Name}' in {nameof(CreateStrategyWithoutHeappe)}.")
        };
    }
}