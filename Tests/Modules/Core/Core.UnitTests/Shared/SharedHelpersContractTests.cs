using System.Reflection;

using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.Workflow.Handlers;

using Forge.Core.Features.Role.Services;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

namespace Core.UnitTests.Shared;

public sealed class SharedHelpersContractTests
{
    private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Assembly CoreAssembly = typeof(Problems).Assembly;

    [Fact]
    public void Should_Return502RoleServiceUnavailable_When_RoleServiceUnavailableIsCalled()
    {
        var problem = Assert.IsType<ProblemHttpResult>(Problems.RoleServiceUnavailable());

        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
        Assert.Equal("upstream.role_service_unavailable", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("Failed to retrieve roles.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void Should_Return403ProviderWriteForbidden_When_ProviderWriteForbiddenIsCalled()
    {
        var problem = Assert.IsType<ProblemHttpResult>(Problems.ProviderWriteForbidden());

        Assert.Equal(StatusCodes.Status403Forbidden, problem.StatusCode);
        Assert.Equal("provider.write_forbidden", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("This identity may not write to this provider.", problem.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData("Member")]
    [InlineData("member")]
    [InlineData("MEMBER")]
    public void Should_FindRoleIgnoringCase_When_NameDiffersOnlyInCase(string name)
    {
        var owner = Role("Owner");
        var member = Role("Member");

        IList<RoleInfo> roles = [owner, member];

        var found = roles.FindByName(name);

        Assert.Same(member, found);
    }

    [Fact]
    public void Should_ReturnNull_When_NoRoleHasTheName()
    {
        IList<RoleInfo> roles = [Role("Owner"), Role("Member")];

        var found = roles.FindByName("Manager");

        Assert.Null(found);
    }

    [Fact]
    public void Should_ReturnNull_When_RoleListIsEmpty()
    {
        IList<RoleInfo> roles = [];

        Assert.Null(roles.FindByName("Owner"));
    }

    [Theory]
    [InlineData("NotFound")]
    [InlineData("Forbidden")]
    [InlineData("Unauthenticated")]
    [InlineData("InternalError")]
    public void Should_NotDeclareUnusedCode_When_CommonErrorCodesIsInspected(string member)
    {
        Assert.Null(typeof(CommonErrorCodes).GetField(member, StaticMembers));
    }

    [Theory]
    [InlineData("CallerNotProvisioned", "caller.not_provisioned")]
    [InlineData("RoleServiceUnavailable", "upstream.role_service_unavailable")]
    public void Should_KeepUsedCode_When_CommonErrorCodesIsInspected(string member, string value)
    {
        var field = typeof(CommonErrorCodes).GetField(member, StaticMembers);

        Assert.NotNull(field);
        Assert.Equal(value, field.GetRawConstantValue());
    }

    [Theory]
    [InlineData("Core.Features.Request.Notifications.CreateAccountRequest")]
    [InlineData("Core.Features.Request.Notifications.CreateAccountDetail")]
    public void Should_NotDeclareDeadNotificationRecord_When_CoreAssemblyIsInspected(string fullName)
    {
        Assert.Null(CoreAssembly.GetType(fullName));
    }

    [Fact]
    public void Should_NotDeclareLocationAllocationResult_When_CreateResourceOnLocationEndpointIsInspected()
    {
        var endpoint = typeof(CreateResourceOnLocationEndpoint);

        Assert.NotNull(endpoint.GetNestedType("Response", BindingFlags.NonPublic));
        Assert.Null(endpoint.GetNestedType("LocationAllocationResult", BindingFlags.NonPublic));
    }

    [Fact]
    public void Should_TakeNoLogger_When_SendEmailHandlerIsConstructed()
    {
        var parameters = typeof(SendEmailHandler).GetConstructors().SelectMany(c => c.GetParameters()).ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(parameters, p => typeof(ILogger).IsAssignableFrom(p.ParameterType));
    }

    private static RoleInfo Role(string name) => new(0, name, name, "Project", [], true, 0);
}
