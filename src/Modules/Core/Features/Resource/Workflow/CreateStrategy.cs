using Core.Features.Resource.Workflow.Steps;

using WorkflowEngine.Contracts.Abstractions;

namespace Core.Features.Resource.Workflow;

/// <summary>
/// Workflow strategy for the create-resource flow (ADR-0057). Orchestrates the sequence:
/// CheckHeappStep → InitializeProjectResourceStep → InitializeHeappeStep → FinalizeCreationProjectResourceStep
/// → SendEmailStep.
/// </summary>
internal sealed class CreateStrategy : IRequestStrategy
{
    public string Name => "create-project-resource";

    public Type InitialStepType => typeof(CheckHeappeStep);

    public StepRequest? Transition(StepRequest completedStep, StepResult? stepResult, IRequestSnapshot request)
    {
        if (stepResult?.hasContinue == false)
            return null;

        return completedStep switch
        {
            CheckHeappeStep s => new InitializeProjectResourceStep(s.RequestId, s.ProviderName, s.Data, s.ResolvedLocations, s.CreatedBy),
            InitializeProjectResourceStep s => new InitializeHeappeStep(s.RequestId, s.ProviderName, s.Data, s.ResolvedLocations),
            InitializeHeappeStep s => new FinalizeCreationProjectResourceStep(s.RequestId, s.ProviderName, s.Data.Name, s.Data.PrimaryInvestigatorEmail, s.Data.AssignedLocations, s.ResolvedLocations, stepResult?.Data),
            FinalizeCreationProjectResourceStep s => new SendEmailStep(s.RequestId, s.Name, s.ProviderName, stepResult?.Data),
            SendEmailStep => null,
            _ => throw new InvalidOperationException($"Unrecognized step type '{completedStep.GetType().Name}' in {nameof(CreateStrategy)}.")
        };
    }
}