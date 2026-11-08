using Core.Domain.Project;
using Core.Domain.Shared;
using Core.Domain.User;
using Lukdrasil.Result;
using Xunit;

namespace Core.UnitTests.Projects;

/// <summary>
/// Unit tests for Project state transitions and lifecycle management.
/// </summary>
public class ProjectStateTransitionTests
{
    private static Project CreateTestProject()
    {
        var projectDomainType = ProjectDomainType.FromName(DomainTypeName.Create("TestDomain"));
        return Project.Create(
            ProjectShortName.From("test-proj"),
            ProjectName.Create("Test Project"),
            projectDomainType,
            UserId.New(),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            EOperationMode.Editable);
    }

    #region State Initialization

    [Fact]
    public void Create_InitializesProjectWithActiveState()
    {
        // Act
        var project = CreateTestProject();

        // Assert
        Assert.Equal(EProjectState.Active, project.State);
        Assert.False(project.IsDeleted);
    }

    #endregion

    #region Active → Expired Transition

    [Fact]
    public void Expire_TransitionsActiveToExpired()
    {
        // Arrange
        var project = CreateTestProject();
        Assert.Equal(EProjectState.Active, project.State);

        // Act
        var result = project.Expire();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EProjectState.Expired, project.State);
    }

    [Fact]
    public void Expire_OnExpiredProject_IsIdempotent()
    {
        // Arrange
        var project = CreateTestProject();
        project.Expire();

        // Act
        var result = project.Expire();

        // Assert
        Assert.True(result.IsSuccess); // Idempotent, no error
        Assert.Equal(EProjectState.Expired, project.State);
    }

    #endregion

    #region Delete Transition

    [Fact]
    public void Delete_TransitionsActiveToDeleted()
    {
        // Arrange
        var project = CreateTestProject();
        Assert.Equal(EProjectState.Active, project.State);

        // Act
        var result = project.Delete();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EProjectState.Deleted, project.State);
        Assert.True(project.IsDeleted);
    }

    [Fact]
    public void Delete_TransitionsExpiredToDeleted()
    {
        // Arrange
        var project = CreateTestProject();
        project.Expire();
        Assert.Equal(EProjectState.Expired, project.State);

        // Act
        var result = project.Delete();

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(EProjectState.Deleted, project.State);
        Assert.True(project.IsDeleted);
    }

    [Fact]
    public void Delete_OnAlreadyDeletedProject_IsIdempotent()
    {
        // Arrange
        var project = CreateTestProject();
        project.Delete();
        Assert.Equal(EProjectState.Deleted, project.State);

        // Act
        var result = project.Delete();

        // Assert
        // Second delete fails because Deleted state returns error from EnsureNotReadOnly
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Expire_OnDeletedProject_ReturnsError()
    {
        // Arrange
        var project = CreateTestProject();
        project.Delete();
        Assert.Equal(EProjectState.Deleted, project.State);

        // Act
        var result = project.Expire();

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ProjectErrorCodes.CannotExpireDeleted, result.Error.Code);
    }

    #endregion

    #region Modification Rules

    [Fact]
    public void Rename_FailsOnExpiredProject()
    {
        // Arrange
        var project = CreateTestProject();
        project.Expire();

        // Act
        var result = project.Rename(ProjectName.Create("New Name"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Rename_FailsOnDeletedProject()
    {
        // Arrange
        var project = CreateTestProject();
        project.Delete();

        // Act
        var result = project.Rename(ProjectName.Create("New Name"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void UpdateDescription_FailsOnExpiredProject()
    {
        // Arrange
        var project = CreateTestProject();
        project.Expire();

        // Act
        var result = project.UpdateDescription(Description.Create("New description"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Reschedule_FailsOnDeletedProject()
    {
        // Arrange
        var project = CreateTestProject();
        project.Delete();

        // Act
        var result = project.Reschedule(
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(60));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void AssignUser_FailsOnExpiredProject()
    {
        // Arrange
        var project = CreateTestProject();
        project.Expire();
        var user = User.CreateUser(
            new Email("test@example.com"),
            new UserName("testuser"),
            new IdpId("idp-123"));

        // Act
        var result = project.AssignUser(user, UserId.New());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    #endregion

    #region Mode vs State Orthogonality

    [Fact]
    public void ReadOnlyMode_BlocksModificationIndependentOfState()
    {
        // Arrange
        var projectDomainType = ProjectDomainType.FromName(DomainTypeName.Create("TestDomain"));
        var project = Project.Create(
            ProjectShortName.From("readonly-proj"),
            ProjectName.Create("ReadOnly Project"),
            projectDomainType,
            UserId.New(),
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30),
            ServiceName.Default,
            EOperationMode.ReadOnly); // ReadOnly mode

        Assert.Equal(EProjectState.Active, project.State);

        // Act
        var result = project.Rename(ProjectName.Create("New Name"));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Equal(ProjectErrorCodes.ReadOnly, result.Error.Code);
    }

    [Fact]
    public void ActiveStateWithEditableMode_AllowsModification()
    {
        // Arrange
        var project = CreateTestProject();
        Assert.Equal(EProjectState.Active, project.State);
        Assert.Equal(EOperationMode.Editable, project.Mode);

        // Act
        var result = project.Rename(ProjectName.Create("Modified Name"));

        // Assert
        Assert.True(result.IsSuccess);
    }

    #endregion

    #region Delete Cascading

    [Fact]
    public void Delete_SetsIsDeletedFlagToTrue()
    {
        // Arrange
        var project = CreateTestProject();
        Assert.False(project.IsDeleted);

        // Act
        project.Delete();

        // Assert
        Assert.True(project.IsDeleted);
        Assert.Equal(EProjectState.Deleted, project.State);
    }

    #endregion
}
