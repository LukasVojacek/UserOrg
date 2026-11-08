using System;
using System.Collections.Generic;

using Core.Domain.Provider;
using Core.Domain.Shared;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow;
using Core.Features.Resource.Workflow.Steps;

using Heappe.Contracts;

using UserOrg.Shared.Persistance.Abstractions.Enums;

using WorkflowEngine.Contracts.Abstractions;
using WorkflowEngine.Contracts.Models;

using Xunit;

using static Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint;

namespace Core.UnitTests.Features.Resource.Workflow;

/// <summary>
/// Unit tests for <see cref="CreateStrategy"/> Transition method.
/// Tests the workflow transitions through all steps: CheckHeappe -> InitializeProjectResource -> InitializeHeappe -> FinalizeCreationProjectResource -> SendEmail.
/// </summary>
public sealed class CreateStrategyTransitionTests
{
    private readonly CreateStrategy _strategy = new();
    private readonly Guid _requestId = Guid.NewGuid();
    private readonly ProviderName _providerName = ProviderName.From("test-provider");
    private readonly string _username = "test-user@example.com";
    private readonly string _resourceName = "test-resource";
    private readonly string _description = "Test resource description";
    private readonly string _piEmail = "pi@example.com";
    private readonly string _accountString = "ACC-123";
    private readonly DateTimeOffset _startDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly DateTimeOffset _endDate = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private Request CreateRequest() => new(
        Name: _resourceName,
        Description: _description,
        PrimaryInvestigatorEmail: _piEmail,
        StartDate: _startDate,
        EndDate: _endDate,
        AccountString: _accountString,
        UnitType: EAllocationUnitType.NodeHours,
        MappingOneToOne: true,
        UseAccountingStringForScheduler: false,
        Usernames: new List<string>() { _username },
        LicenseUrl: null,
        BypassHeappe: false,
        Clusters: new List<Cluster>
        {
            new(ClusterName: "cluster-1", ScratchPath: "/scratch", ProjectPath: "/project")
        },
        AssignedLocations: new List<LocationAssignment>
        {
            new(
                LocationName: "location-1",
                AllocationUnitType: EAllocationUnitType.NodeHours,
                AggregationName: null,
                AllocationUnitCount: 100,
                AllocationDescription: "Test allocation",
                PreferredAuthenticationType: "pwd",
                Specifications: null
            )
        }
    );

    private CheckHeappeStep CreateCheckHeappeStep()
    {
        var request = CreateRequest();
        var resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };

        return new CheckHeappeStep(
            RequestId: _requestId,
            ProviderName: _providerName,
            Data: request,
            ResolvedLocations: resolvedLocations,
            CreatedBy: UserId.Create(Guid.NewGuid())
        );
    }

    [Fact(DisplayName = "Transition_CheckHeappeStep_ReturnsInitializeProjectResourceStep")]
    public void Transition_CheckHeappeStep_ReturnsInitializeProjectResourceStep()
    {
        // Arrange
        var completedStep = CreateCheckHeappeStep();
        var request = completedStep.Data;
        var providerName = completedStep.ProviderName;

        // Act
        var result = _strategy.Transition(completedStep, null, null!);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<InitializeProjectResourceStep>(result);

        var nextStep = (InitializeProjectResourceStep)result;
        Assert.Equal(_requestId, nextStep.RequestId);
        Assert.Equal(providerName, nextStep.ProviderName);
        Assert.Equal(request, nextStep.Data);
    }

    [Fact(DisplayName = "Transition_InitializeProjectResourceStep_ReturnsInitializeHeappeStep")]
    public void Transition_InitializeProjectResourceStep_ReturnsInitializeHeappeStep()
    {
        // Arrange
        var request = CreateRequest();
        var resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };

        var completedStep = new InitializeProjectResourceStep(
            RequestId: _requestId,
            ProviderName: _providerName,
            Data: request,
            ResolvedLocations: resolvedLocations,
            CreatedBy: UserId.Create(Guid.NewGuid())
        );

        // Act
        var result = _strategy.Transition(completedStep, null, null!);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<InitializeHeappeStep>(result);

        var nextStep = (InitializeHeappeStep)result;
        Assert.Equal(_requestId, nextStep.RequestId);
        Assert.Equal(_resourceName, nextStep.Data.Name);
        Assert.Equal(_description, nextStep.Data.Description);
        Assert.Equal(_piEmail, nextStep.Data.PrimaryInvestigatorEmail);
        Assert.Equal(_startDate, nextStep.Data.StartDate);
        Assert.Equal(_endDate, nextStep.Data.EndDate);
        Assert.Equal(_accountString, nextStep.Data.AccountString);
        Assert.Equal(EAllocationUnitType.NodeHours, nextStep.Data.UnitType);
        Assert.True(nextStep.Data.MappingOneToOne);
        Assert.False(nextStep.Data.UseAccountingStringForScheduler);
        Assert.NotEmpty(nextStep.Data.Clusters);
        Assert.NotEmpty(nextStep.Data.AssignedLocations);
    }

    [Fact(DisplayName = "Transition_InitializeHeappeStep_ReturnsInitializeHeappeStep_With_Default_Clusters")]
    public void Transition_InitializeHeappeStep_ReturnsInitializeHeappeStep_With_Default_Clusters()
    {
        // Arrange
        var request = CreateRequest();
        var resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };
        var assignedLocations = request.AssignedLocations;

        var completedStep = new InitializeHeappeStep(
            RequestId: _requestId,
            ProviderName: _providerName,
            new Request(
            Name: _resourceName,
            Description: _description,
            PrimaryInvestigatorEmail: _piEmail,
            StartDate: _startDate,
            EndDate: _endDate,
            AccountString: _accountString,
            UnitType: EAllocationUnitType.NodeHours,
            MappingOneToOne: true,
            UseAccountingStringForScheduler: false,
            Usernames: new List<string>() { _username },
            BypassHeappe: false,
            LicenseUrl: "https://example.com/license",
            Clusters: new List<Cluster>
            {
                new(ClusterName: "cluster-1", ScratchPath: "/scratch", ProjectPath: "/project")
            },
            AssignedLocations: assignedLocations),
            ResolvedLocations: resolvedLocations
        );

        // Act
        var result = _strategy.Transition(completedStep, null, null!);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<FinalizeCreationProjectResourceStep>(result);

        var nextStep = (FinalizeCreationProjectResourceStep)result;
        Assert.Equal(_requestId, nextStep.RequestId);
        Assert.Equal(_resourceName, nextStep.Name);
        Assert.Equal(_piEmail, nextStep.PrimaryInvestigatorEmail);
        Assert.NotEmpty(nextStep.AssignedLocations);
    }

    [Fact(DisplayName = "Transition_FinalizeCreationProjectResourceStep_ReturnsSendEmailStep")]
    public void Transition_FinalizeCreationProjectResourceStep_ReturnsSendEmailStep()
    {
        // Arrange
        var request = CreateRequest();
        var resolvedLocations = new List<ResolvedLocationRef>
        {
            new(LocationName: "location-1", LocationType: Domain.Shared.ELocationType.HPC, Endpoint: "https://heappe-endpoint", heappeUrl: "https://heappe-endpoint")
        };
        var completedStep = new FinalizeCreationProjectResourceStep(
            RequestId: _requestId,
            ProviderName: _providerName,
            Name: _resourceName,
            PrimaryInvestigatorEmail: _piEmail,
            AssignedLocations: request.AssignedLocations,
            ResolvedLocations: resolvedLocations,
            Data: null
        );

        // Act
        var result = _strategy.Transition(completedStep, null, null!);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<SendEmailStep>(result);

        var nextStep = (SendEmailStep)result;
        Assert.Equal(_requestId, nextStep.RequestId);
        Assert.Equal(_resourceName, nextStep.Name);
        Assert.Equal(_providerName, nextStep.ProviderName);
    }

    /// <summary>
    /// SendEmailStep is the terminal step of the create flow: Transition returns null after it, which is how
    /// RequestWorker recognises the flow is finished and marks the request Completed. Throwing here (the
    /// earlier behaviour) failed every otherwise successful create-resource request on its last step.
    /// </summary>
    [Fact(DisplayName = "Transition_SendEmailStep_ReturnsNull")]
    public void Transition_SendEmailStep_ReturnsNull()
    {
        // Arrange
        var completedStep = new SendEmailStep(
            RequestId: _requestId,
            Name: _resourceName,
            ProviderName: _providerName,
            Data: null
        );

        // Act
        var result = _strategy.Transition(completedStep, null, null!);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// The catch-all arm still throws for a step belonging to a different strategy — a real
    /// <see cref="StepRequest"/> from the assign-resource flow, not a test-only stand-in.
    /// </summary>
    [Fact(DisplayName = "Transition_StepFromAnotherStrategy_ThrowsInvalidOperationException")]
    public void Transition_StepFromAnotherStrategy_ThrowsInvalidOperationException()
    {
        // Arrange
        var foreignStep = new Core.Features.ResourceRequest.Flow.SendPiApprovalEmailStep(_requestId, "some-slug");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _strategy.Transition(foreignStep, null, null!)
        );

        Assert.Contains("Unrecognized step type 'SendPiApprovalEmailStep'", exception.Message);
        Assert.Contains(nameof(CreateStrategy), exception.Message);
    }

    [Fact(DisplayName = "Transition_UnrecognizedStepType_ThrowsInvalidOperationException")]
    public void Transition_UnrecognizedStepType_ThrowsInvalidOperationException()
    {
        // Arrange
        var unrecognizedStep = new UnrecognizedStep(_requestId);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            _strategy.Transition(unrecognizedStep, null, null!)
        );

        Assert.Contains("Unrecognized step type 'UnrecognizedStep'", exception.Message);
        Assert.Contains(nameof(CreateStrategy), exception.Message);
    }

    [Fact(DisplayName = "CreateStrategy_InitialStepType_ReturnsCheckHeappeStep")]
    public void CreateStrategy_InitialStepType_ReturnsCheckHeappeStep()
    {
        // Arrange & Act
        var initialStepType = _strategy.InitialStepType;

        // Assert
        Assert.Equal(typeof(CheckHeappeStep), initialStepType);
    }

    [Fact(DisplayName = "CreateStrategy_Name_ReturnsCreateProjectResourceName")]
    public void CreateStrategy_Name_ReturnsCreateProjectResourceName()
    {
        // Arrange & Act
        var strategyName = _strategy.Name;

        // Assert
        Assert.Equal("create-project-resource", strategyName);
    }

    private sealed record UnrecognizedStep(Guid RequestId) : StepRequest(RequestId, "UnrecognizedStep");
}
