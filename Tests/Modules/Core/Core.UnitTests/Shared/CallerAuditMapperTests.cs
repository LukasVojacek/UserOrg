using Core.Shared.Auth;

using Lukdrasil.StepUpLogging;

namespace Core.UnitTests.Shared;

public sealed class CallerAuditMapperTests
{
    private static readonly Caller TestCaller = new(
        new IdpId("u1"),
        "Owner",
        "Jane Doe",
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        Email: Email.Create("jane.doe@example.com"),
        RawSub: "u1@eudat.eu");

    private static readonly Caller ServiceKeyCaller = new(
        new IdpId("svc1"),
        "Provider",
        Name: null,
        new Dictionary<string, IReadOnlySet<ProjectShortName>>(),
        RawSub: "svc1@eudat.eu",
        IsServiceKeyIdentity: true);

    [Fact]
    public void Should_ComposeAuditEvent_When_MappingCaller()
    {
        var tenant = ProjectShortName.Create("PROJ-1");

        var auditEvent = CallerAuditMapper.ComposeSuccess(AuditActions.ProjectUserRoleChanged, TestCaller, tenant);

        Assert.Equal("u1", auditEvent.ActorId);
        Assert.Equal("user", auditEvent.ActorType);
        Assert.Equal("PROJ-1", auditEvent.TenantId);
        Assert.Equal("u1@eudat.eu", auditEvent.Data!["sub"]);
    }

    [Fact]
    public void Should_CarryOnlySub_When_CallerHasEmailAndName()
    {
        var auditEvent = CallerAuditMapper.ComposeSuccess(AuditActions.ProjectUserRoleChanged, TestCaller);

        Assert.Equal(["sub"], auditEvent.Data!.Keys);
        Assert.DoesNotContain(auditEvent.Data!.Values, value => Equals(value, TestCaller.Email!.Value.Value));
        Assert.DoesNotContain(auditEvent.Data!.Values, value => Equals(value, TestCaller.Name));
    }

    [Fact]
    public void Should_AddNothingBeyondSub_When_CallerSuppliesData()
    {
        var auditEvent = CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectUserRoleChanged,
            TestCaller,
            data: new Dictionary<string, object?> { ["old_role"] = "Member", ["new_role"] = "Manager" });

        Assert.Equal(["new_role", "old_role", "sub"], auditEvent.Data!.Keys.Order());
        Assert.Equal("u1@eudat.eu", auditEvent.Data!["sub"]);
        Assert.DoesNotContain(auditEvent.Data!.Values, value => Equals(value, TestCaller.Email!.Value.Value));
        Assert.DoesNotContain(auditEvent.Data!.Values, value => Equals(value, TestCaller.Name));
    }

    [Fact]
    public void Should_Throw_When_CallerDataClaimsTheSubKey()
    {
        var exception = Assert.Throws<ArgumentException>(() => CallerAuditMapper.ComposeSuccess(
            AuditActions.ProjectUserRoleChanged,
            TestCaller,
            data: new Dictionary<string, object?> { ["sub"] = "impostor@eudat.eu" }));

        Assert.Equal("data", exception.ParamName);
    }

    [Fact]
    public void Should_LeaveTenantIdNull_When_OperationIsNotProjectScoped()
    {
        var auditEvent = CallerAuditMapper.ComposeSuccess(AuditActions.ProjectUserRoleChanged, TestCaller);

        Assert.Null(auditEvent.TenantId);
    }

    [Fact]
    public void Should_ComposeDeniedAuditEvent_When_MappingCallerForDenial()
    {
        var auditEvent = CallerAuditMapper.ComposeDenied(AuditActions.ProjectUserRoleChanged, TestCaller);

        Assert.Equal(AuditOutcome.Denied, auditEvent.Outcome);
        Assert.Equal("u1", auditEvent.ActorId);
    }

    [Fact]
    public void Should_RecordServiceActorType_When_CallerAuthenticatedWithServiceKey()
    {
        var auditEvent = CallerAuditMapper.ComposeSuccess(AuditActions.ProjectUserRoleChanged, ServiceKeyCaller);

        Assert.Equal("service", auditEvent.ActorType);
    }

    [Fact]
    public void Should_RecordServiceActorType_When_ServiceKeyCallerIsDenied()
    {
        var auditEvent = CallerAuditMapper.ComposeDenied(AuditActions.ProjectUserRoleChanged, ServiceKeyCaller);

        Assert.Equal("service", auditEvent.ActorType);
    }
}
