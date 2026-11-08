using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.CreateProjectCommandTemplate.Request;
using Heappe.Helpers.Connection;
using Heappe.Persistence.Entities;
using Heappe.Persistence.Enums;

using Lukdrasil.Result;

using Moq;

using UserOrg.Shared;

using Xunit;

using static Heappe.Features.CreateProjectCommandTemplate.CreateTemplateEndpoint;

using Error = Lukdrasil.Error.Error;
using TemplateErrors = Heappe.Features.CreateProjectCommandTemplate.Errors;

namespace Heappe.UnitTests.Features.CreateProjectCommandTemplate;

/// <summary>
/// Unit tests for <see cref="CreateTemplateFlowExtensions"/>.
/// Covers the <c>BuildCommandTemplateRequest</c> and <c>CreateCommandTemplateAsync</c> pipeline steps.
/// All tests run in-process without external dependencies.
/// </summary>
public sealed class CreateTemplateFlowExtensionsTests
{

    private static readonly Guid ProjectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid ResourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Guid LocationId = Guid.Parse("33333333-0000-0000-0000-000000000000");

    private static PreparedResourceData CreatePreparedData(LocationPartition partition, string shortName = "tp")
        => new(
            Project: new ProjectResourceInfoEntry(
                ProjectId, "TestProject", shortName, null,
                DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                new ResourceInfoEntry(Guid.NewGuid(), "resource", null, null, null)),
            Resource: new ProjectResourceInfoSyncEntry(
                Guid.NewGuid(), "TestProject", shortName, null,
                DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                new ResourceInfoSyncEntry(ResourceId, "res", null, null, null, [])),
            LocationId: LocationId,
            LocationName: "location1",
            ConnectionInfo: new ServiceTokenHeappeConnectionInfo(new Uri("http://heappe.test"), new HeappeIdentifier("heappe-test"), "token"),
            Partition: partition);

    /// <summary>Creates a partition with one loaded <see cref="ProjectCommandTemplate"/> for the given ids.</summary>
    private static LocationPartition CreatePartitionWithTemplate(string templateName, Guid projectId, Guid resourceId)
    {
        var partition = new LocationPartition
        {
            Id = Guid.NewGuid(),
            Name = "queue1",
            LocationId = LocationId
        };

        var template = ProjectCommandTemplate.Create(templateName, ECommandTemplateType.UserDefined, projectId, resourceId);
        var templatePartition = new ProjectCommandTemplatePartition
        {
            Id = Guid.NewGuid(),
            LocationPartition = partition,
            ProjectCommandTemplate = template,
            State = ECommandTemplateState.Active
        };
        partition.ProjectCommandTemplatePartitions.Add(templatePartition);
        return partition;
    }

    private static LocationPartition CreateEmptyPartition()
        => new() { Id = Guid.NewGuid(), Name = "queue1", LocationId = LocationId };

    private static ProjectModel CreateHeappeProject(params CommandTemplateCompactModel[] templates)
        => new(
            Id: 100L, Name: "TestProject", Description: "", AccountingString: "acc",
            StartDate: DateTime.UtcNow, EndDate: DateTime.UtcNow.AddYears(1),
            UsageType: 0, UseAccountingStringForScheduler: false,
            KeyScriptsDirectoryPath: "/scripts", IsOneToOneMapping: false,
            CommandTemplates: templates.ToList());

    private static NodeTypeModel CreateNodeType()
        => new(
            Id: 1L, Name: "compute", Description: "",
            NumberOfNodes: 1, CoresPerNode: 8, MaxWalltime: 3600,
            FileTransferMethodId: 1, Queue: "queue1", QualityOfService: "",
            ClusterAllocationName: "", ClusterNodeTypeAggregation: null,
            Accounting: [], Projects: []);

    private static CreateTemplateRequest CreateRequest(string name = "MyTemplate", string shortName = "tp")
        => new(
            ProjectIdentifier: new ProjectResourceTarget(shortName, "resource"),
            PartitionName: "queue1",
            LocationName: "location1",
            AggregationName: null,
            Name: name,
            Description: null,
            PreparationScriptPath: null,
            ExecutionScriptPath: "/scripts/run.sh",
            CommandTemplateParameters: []);

    private static Task<Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>> SuccessTask(
        PreparedResourceData prepared, ProjectModel heappeProject, NodeTypeModel nodeType)
        => Task.FromResult(
            Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>
                .Success((prepared, heappeProject, nodeType)));

    private static Task<Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>> FailureTask(Error error)
        => Task.FromResult(
            Result<(PreparedResourceData, ProjectModel, NodeTypeModel), Error>.Failure(error));

    [Fact(DisplayName = "BuildCommandTemplateRequest: both ExistingHeappeTemplate and ExistingProjectTemplate are null when neither template exists")]
    public async Task BuildCommandTemplateRequest_Should_ReturnBothNull_When_NeitherTemplateExists()
    {
        // Arrange
        var upstream = SuccessTask(
            CreatePreparedData(CreateEmptyPartition()),
            CreateHeappeProject(),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExistingHeappeTemplate);
        Assert.Null(result.Value.ExistingProjectTemplate);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: ExistingHeappeTemplate is set when only HEAppE template exists")]
    public async Task BuildCommandTemplateRequest_Should_SetExistingHeappeTemplate_When_OnlyHeappeTemplateExists()
    {
        // Arrange
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var heappeTemplate = new CommandTemplateCompactModel(1L, prefixed, "", "", false, true, null, []);
        var upstream = SuccessTask(
            CreatePreparedData(CreateEmptyPartition(), shortName),
            CreateHeappeProject(heappeTemplate),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ExistingHeappeTemplate);
        Assert.Equal(prefixed, result.Value.ExistingHeappeTemplate!.Name);
        Assert.Null(result.Value.ExistingProjectTemplate);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: ExistingProjectTemplate is set when only DB template exists")]
    public async Task BuildCommandTemplateRequest_Should_SetExistingProjectTemplate_When_OnlyDbTemplateExists()
    {
        // Arrange
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var partition = CreatePartitionWithTemplate(prefixed, ProjectId, ResourceId);
        var upstream = SuccessTask(
            CreatePreparedData(partition, shortName),
            CreateHeappeProject(),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ExistingProjectTemplate);
        Assert.Equal(prefixed, result.Value.ExistingProjectTemplate!.Name);
        Assert.Null(result.Value.ExistingHeappeTemplate);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: both templates set when both already exist")]
    public async Task BuildCommandTemplateRequest_Should_SetBothTemplates_When_BothExist()
    {
        // Arrange
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var heappeTemplate = new CommandTemplateCompactModel(2L, prefixed, "", "", false, true, null, []);
        var partition = CreatePartitionWithTemplate(prefixed, ProjectId, ResourceId);
        var upstream = SuccessTask(
            CreatePreparedData(partition, shortName),
            CreateHeappeProject(heappeTemplate),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ExistingProjectTemplate);
        Assert.NotNull(result.Value.ExistingHeappeTemplate);
        Assert.Equal(prefixed, result.Value.ExistingProjectTemplate!.Name);
        Assert.Equal(prefixed, result.Value.ExistingHeappeTemplate!.Name);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: DB template not matched when ProjectId differs")]
    public async Task BuildCommandTemplateRequest_Should_NotMatchDbTemplate_When_ProjectIdDiffers()
    {
        // Arrange
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var partition = CreatePartitionWithTemplate(prefixed, Guid.NewGuid(), ResourceId);
        var upstream = SuccessTask(
            CreatePreparedData(partition, shortName),
            CreateHeappeProject(),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExistingProjectTemplate);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: DB template not matched when ResourceId differs")]
    public async Task BuildCommandTemplateRequest_Should_NotMatchDbTemplate_When_ResourceIdDiffers()
    {
        // Arrange
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var partition = CreatePartitionWithTemplate(prefixed, ProjectId, Guid.NewGuid());
        var upstream = SuccessTask(
            CreatePreparedData(partition, shortName),
            CreateHeappeProject(),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.ExistingProjectTemplate);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: ShortName prefix is applied to template name in created request")]
    public async Task BuildCommandTemplateRequest_Should_ApplyShortNamePrefix_ToTemplateName()
    {
        // Arrange
        const string shortName = "myproj";
        const string templateBase = "SomeTemplate";

        var upstream = SuccessTask(
            CreatePreparedData(CreateEmptyPartition(), shortName),
            CreateHeappeProject(),
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal($"{shortName}_{templateBase}", result.Value.CreateModel!.Name);
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: upstream failure is propagated without processing")]
    public async Task BuildCommandTemplateRequest_Should_PropagateFailure_When_UpstreamFails()
    {
        // Arrange
        var error = TemplateErrors.PartitionNotFound();
        var upstream = FailureTask(error);

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact(DisplayName = "CreateCommandTemplateAsync: delegates to service and returns success")]
    public async Task CreateCommandTemplateAsync_Should_DelegateToService_And_ReturnSuccess()
    {
        // Arrange
        var serviceMock = new Mock<IProjectCommandTemplateCreateService>(MockBehavior.Strict);
        var creationRequest = BuildMinimalCreationRequest();
        var cache = new FakeHybridCache();

        serviceMock
            .Setup(s => s.CreateCommandTemplateAsync(creationRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Success())
            .Verifiable();

        var upstream = Task.FromResult(
            Result<CommandTemplateCreationRequest, Error>.Success(creationRequest));

        // Act
        var result = await upstream.CreateCommandTemplateAsync(serviceMock.Object, cache, "tp", CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        serviceMock.Verify();
    }

    [Fact(DisplayName = "CreateCommandTemplateAsync: service failure is propagated")]
    public async Task CreateCommandTemplateAsync_Should_PropagateFailure_When_ServiceFails()
    {
        // Arrange
        var error = TemplateErrors.FailedToCreateProjectCommandTemplate(
            "tp_Test", ECommandTemplateType.UserDefined, ProjectId, ResourceId);
        var serviceMock = new Mock<IProjectCommandTemplateCreateService>(MockBehavior.Strict);
        var creationRequest = BuildMinimalCreationRequest();
        var cache = new FakeHybridCache();

        serviceMock
            .Setup(s => s.CreateCommandTemplateAsync(creationRequest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Error>.Failure(error));

        var upstream = Task.FromResult(
            Result<CommandTemplateCreationRequest, Error>.Success(creationRequest));

        // Act
        var result = await upstream.CreateCommandTemplateAsync(serviceMock.Object, cache, "tp", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact(DisplayName = "CreateCommandTemplateAsync: upstream failure is propagated and service is not called")]
    public async Task CreateCommandTemplateAsync_Should_PropagateFailure_And_NotCallService_When_UpstreamFails()
    {
        // Arrange – MockBehavior.Strict: any unexpected call throws
        var error = TemplateErrors.NodeTypeForQueueNotFound();
        var serviceMock = new Mock<IProjectCommandTemplateCreateService>(MockBehavior.Strict);
        var cache = new FakeHybridCache();

        var upstream = Task.FromResult(
            Result<CommandTemplateCreationRequest, Error>.Failure(error));

        // Act
        var result = await upstream.CreateCommandTemplateAsync(serviceMock.Object, cache, "tp", CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        serviceMock.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "BuildCommandTemplateRequest: ExistingProjectTemplate set and ExistingHeappeTemplate null triggers HandleCreateHeappeOnlyAsync path via CreateModel (regression – must not throw)")]
    public async Task BuildCommandTemplateRequest_Should_ProduceCreateModel_Request_When_DbExistsHeappeMissing()
    {
        // Arrange – template in DB, not in HEAppE → scenario 2: shouldCreateHeappe=true, shouldCreateLocal=false
        const string shortName = "tp";
        const string templateBase = "MyTemplate";
        var prefixed = $"{shortName}_{templateBase}";

        var partition = CreatePartitionWithTemplate(prefixed, ProjectId, ResourceId);
        var upstream = SuccessTask(
            CreatePreparedData(partition, shortName),
            CreateHeappeProject(),   // no HEAppE template
            CreateNodeType());

        // Act
        var result = await upstream.BuildCommandTemplateRequest(CreateRequest(templateBase, shortName));

        // Assert – CreateModel must be set (not null) so HandleCreateHeappeOnlyAsync can use it without throwing
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ExistingProjectTemplate);
        Assert.Null(result.Value.ExistingHeappeTemplate);
        Assert.NotNull(result.Value.CreateModel);       // ← service cesta musí mít CreateModel
        Assert.Null(result.Value.TemplateDefinition);   // ← TemplateDefinition nesmí být nastaveno z HTTP cesty
    }

    private static CommandTemplateCreationRequest BuildMinimalCreationRequest()
    {
        var cmdModel = new CommandTemplateCreateModel(
            Name: "tp_Test",
            Description: null,
            ExtendedAllocationCommand: null,
            ExecutableFile: "/scripts/run.sh",
            PreparationScript: null,
            ProjectId: 100L,
            ClusterNodeTypeId: 1L,
            TemplateParameters: [],
            SessionCode: null);

        return CommandTemplateCreationRequest.WithCreateModel(
            cmdModel,
            new PublicHeappeConnectionInfo(new Uri("http://heappe.test"), new HeappeIdentifier("heappe-test")),
            CreateHeappeProject(),
            CreateNodeType(),
            LocationId,
            ProjectId,
            ResourceId);
    }
}
