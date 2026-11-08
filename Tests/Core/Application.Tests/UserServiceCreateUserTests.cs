using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Logging;

using Moq;

using UserOrg.Shared;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.IRepositories;

namespace UserOrganizationManagement.Application.Tests;

public sealed class UserServiceCreateUserTests
{
    [Fact]
    public async Task CreateUser_CreatesUserAndAssignsPlatformUserRole()
    {
        // Arrange
        var mockUow = new Mock<IUserOrgUnitOfWork>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserPermissionRepo = new Mock<IUserPermissionRepository>();
        var mockRoleListService = new Mock<IRoleListService>();
        var mockLogger = new Mock<ILogger<UserService>>();

        mockUow.Setup(u => u.UserRepository).Returns(mockUserRepo.Object);
        mockUow.Setup(u => u.UserPermissionRepository).Returns(mockUserPermissionRepo.Object);
        mockUow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Simulate that users already exist (not first user)
        mockUserRepo.Setup(r => r.TotalCount(It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var platformUserRole = new RoleInfo(
            Id: 1,
            Name: RoleInfoConstants.PlatformUserRoleName,
            Description: "Platform User",
            Category: "Platform",
            Permissions: [],
            IsActive: true,
            Level: 1
        );

        IList<RoleInfo> roleList = [platformUserRole];
        var successResult = Result<IList<RoleInfo>, Error>.Success(roleList, State.Ok);

        mockRoleListService.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        var userService = new UserService(mockUow.Object, mockRoleListService.Object, mockLogger.Object);

        var newUser = new User
        {
            IdentityProviderUserId = "test-user-123",
            Email = "test@example.com",
            UserName = "testuser"
        };

        // Act
        await userService.CreateUser(newUser, CancellationToken.None);

        // Assert
        mockUserRepo.Verify(r => r.InsertAsync(newUser, It.IsAny<CancellationToken>()), Times.Once);
        mockUserPermissionRepo.Verify(r => r.InsertAsync(
            It.Is<UserPermission>(up =>
                up.User == newUser &&
                up.SystemRoleId == platformUserRole.Id &&
                up.Project == null),
            It.IsAny<CancellationToken>()),
            Times.Once);
        mockUow.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateUser_ThrowsException_WhenPlatformUserRoleNotFound()
    {
        // Arrange
        var mockUow = new Mock<IUserOrgUnitOfWork>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserPermissionRepo = new Mock<IUserPermissionRepository>();
        var mockRoleListService = new Mock<IRoleListService>();
        var mockLogger = new Mock<ILogger<UserService>>();

        mockUow.Setup(u => u.UserRepository).Returns(mockUserRepo.Object);
        mockUow.Setup(u => u.UserPermissionRepository).Returns(mockUserPermissionRepo.Object);

        // Simulate that users exist (not first user)
        mockUserRepo.Setup(r => r.TotalCount(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        // Return empty role list (PlatformUser role not found)
        IList<RoleInfo> emptyRoleList = [];
        var successResult = Result<IList<RoleInfo>, Error>.Success(emptyRoleList, State.Ok);

        mockRoleListService.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        var userService = new UserService(mockUow.Object, mockRoleListService.Object, mockLogger.Object);

        var newUser = new User
        {
            IdentityProviderUserId = "test-user-456",
            Email = "test2@example.com",
            UserName = "testuser2"
        };

        // Act & Assert
        await Assert.ThrowsAsync<Domain.Exceptions.SystemRoleNotFoundException>(
            () => userService.CreateUser(newUser, CancellationToken.None));
    }

    // todo this test should set up platform admin not provider
    [Fact]
    public async Task CreateUser_WhenNoUsersExist_AssignsProviderAdministratorRole()
    {
        // Arrange
        var mockUow = new Mock<IUserOrgUnitOfWork>();
        var mockUserRepo = new Mock<IUserRepository>();
        var mockUserPermissionRepo = new Mock<IUserPermissionRepository>();
        var mockRoleListService = new Mock<IRoleListService>();
        var mockLogger = new Mock<ILogger<UserService>>();

        mockUow.Setup(u => u.UserRepository).Returns(mockUserRepo.Object);
        mockUow.Setup(u => u.UserPermissionRepository).Returns(mockUserPermissionRepo.Object);
        mockUow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Simulate that this is the first user (count = 0)
        mockUserRepo.Setup(r => r.TotalCount(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var platformAdminRole = new RoleInfo(
            Id: 2,
            Name: RoleInfoConstants.PlatformAdministratorRoleName,
            Description: "Platform Administrator",
            Category: "Platform",
            Permissions: [],
            IsActive: true,
            Level: 2
        );

        IList<RoleInfo> roleList = [platformAdminRole];
        var successResult = Result<IList<RoleInfo>, Error>.Success(roleList, State.Ok);

        mockRoleListService.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(successResult);

        var userService = new UserService(mockUow.Object, mockRoleListService.Object, mockLogger.Object);

        var newUser = new User
        {
            IdentityProviderUserId = "first-user-789",
            Email = "admin@example.com",
            UserName = "adminuser"
        };

        // Act
        await userService.CreateUser(newUser, CancellationToken.None);

        // Assert
        mockUserRepo.Verify(r => r.InsertAsync(newUser, It.IsAny<CancellationToken>()), Times.Once);
        mockUserPermissionRepo.Verify(r => r.InsertAsync(
            It.Is<UserPermission>(up =>
                up.User == newUser &&
                up.SystemRoleId == platformAdminRole.Id &&
                up.Project == null),
            It.IsAny<CancellationToken>()),
            Times.Once);
        mockUow.Verify(u => u.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("First user created")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}