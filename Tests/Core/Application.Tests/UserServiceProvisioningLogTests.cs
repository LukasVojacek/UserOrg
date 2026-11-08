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

public sealed class UserServiceProvisioningLogTests
{
    private const string Email = "first-user@example.org";
    private const string IdpUserId = "idp-first-user";

    [Fact]
    public async Task Should_LogTheIdpUserIdButNotTheEmail_When_TheFirstUserIsProvisioned()
    {
        var uow = new Mock<IUserOrgUnitOfWork>();
        var users = new Mock<IUserRepository>();
        uow.Setup(u => u.UserRepository).Returns(users.Object);
        uow.Setup(u => u.UserPermissionRepository).Returns(new Mock<IUserPermissionRepository>().Object);
        uow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        users.Setup(r => r.TotalCount(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        IList<RoleInfo> roles = [new(2, RoleInfoConstants.PlatformAdministratorRoleName, "Platform Administrator", "Platform", [], true, 2)];
        var roleList = new Mock<IRoleListService>();
        roleList.Setup(r => r.ListRoles(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IList<RoleInfo>, Error>.Success(roles, State.Ok));

        var logger = new Mock<ILogger<UserService>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        await new UserService(uow.Object, roleList.Object, logger.Object)
            .CreateUser(new User { IdentityProviderUserId = IdpUserId, Email = Email, UserName = "first" }, CancellationToken.None);

        var messages = logger.Invocations
            .Where(invocation => invocation.Method.Name == nameof(ILogger.Log))
            .Select(invocation => invocation.Arguments[2]?.ToString() ?? string.Empty)
            .ToList();
        Assert.Contains(messages, message => message.Contains(IdpUserId, StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message => message.Contains(Email, StringComparison.OrdinalIgnoreCase));
    }
}