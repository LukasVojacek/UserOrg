using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Audit;
using Heappe.Contracts.HEappeApiDto.Responses;
using Heappe.Features.Connection;
using Heappe.Features.ResourceValidityAudit;
using Heappe.Helpers.Connection;
using Heappe.HostContracts;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.Options;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests.Features.ResourceValidityAudit;

/// <summary>
/// Unit tests for <see cref="HeappeResourceAuditService"/>. Behaviour is verified through the public
/// <c>RunAuditAsync</c> stream with the three ports (target provider, connection factory, HEAppE) mocked.
/// </summary>
public sealed class HeappeResourceAuditServiceTests
{
    private static readonly Uri Instance = new("https://heappe.example.org/");
    private static readonly HeappeIdentifier Identifier = new("heappe-1");

    private readonly Mock<IHeappeAuditTargetProvider> _targets = new(MockBehavior.Strict);
    private readonly Mock<IHeappeConnectionFactory> _connections = new(MockBehavior.Strict);
    private readonly Mock<IHeappeService> _heappe = new(MockBehavior.Strict);

    private HeappeResourceAuditService CreateSut() =>
        new(
            _targets.Object,
            _connections.Object,
            _heappe.Object,
            Options.Create(new TokenExchangeOptions { Nexus = { BaseUrl = new Uri("https://nexus.example:8443/") } }));

    private static HeappeAuditTargetEntry Target(
        string resourceName = "RES-1",
        string projectShortName = "projA",
        Uri? uri = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null) =>
        new(resourceName, projectShortName, uri ?? Instance, Identifier,
            start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            end ?? new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

    private static ProjectModel HeappeProject(DateTime start, DateTime end) =>
        new(1, "RES-1", "desc", "RES-1", start, end, 1, true, "/keys", true, []);

    private void SetupTargets(params HeappeAuditTargetEntry[] targets) =>
        _targets.Setup(t => t.GetAuditTargetsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(targets.ToList());

    private void SetupCredentialsOk() =>
        _connections.Setup(c => c.ForServiceAsync(Identifier, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(new ServiceTokenHeappeConnectionInfo(Instance, Identifier, "key"), State.Ok));

    private void SetupHeappeProject(string resourceName, ProjectModel project) =>
        _heappe.Setup(h => h.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), resourceName, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<ProjectModel, Error>.Success(project, State.Ok));

    private async Task<List<TargetAuditResult>> RunAsync()
    {
        var results = new List<TargetAuditResult>();
        await foreach (var r in CreateSut().RunAuditAsync(CancellationToken.None).ConfigureAwait(false))
        {
            results.Add(r);
        }

        return results;
    }

    [Fact(DisplayName = "Yields Ok when the HEAppE project exists with matching validity")]
    public async Task Should_Yield_Ok_When_Project_Exists_With_Matching_Validity()
    {
        SetupTargets(Target());
        SetupCredentialsOk();
        SetupHeappeProject("RES-1", HeappeProject(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        var results = await RunAsync();

        var result = Assert.Single(results);
        Assert.Equal(AuditStatus.Ok, result.Status);
        Assert.Equal("RES-1", result.ResourceName);
        Assert.Equal("projA", result.ProjectShortName);
        Assert.Equal(Instance, result.HeappeUri);
        Assert.False(result.IsFinding);
    }

    [Fact(DisplayName = "Labels a target without a URL with the proxy of TokenExchange:Nexus:BaseUrl")]
    public async Task Should_Label_A_Target_Without_Url_With_The_Proxy_Of_The_Configured_Base_Url()
    {
        SetupTargets(new HeappeAuditTargetEntry(
            "RES-1",
            "projA",
            null,
            Identifier,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        SetupCredentialsOk();
        SetupHeappeProject("RES-1", HeappeProject(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc)));

        var results = await RunAsync();

        var result = Assert.Single(results);
        Assert.Equal(AuditStatus.Ok, result.Status);
        Assert.Equal(new Uri("https://nexus.example:8443/api/heappe/heappe-1/"), result.HeappeUri);
    }

    [Fact(DisplayName = "Labels a target that cannot be checked without a URL with the proxy of TokenExchange:Nexus:BaseUrl")]
    public async Task Should_Label_A_Failed_Check_Without_Url_With_The_Proxy_Of_The_Configured_Base_Url()
    {
        SetupTargets(new HeappeAuditTargetEntry(
            "RES-1",
            "projA",
            null,
            Identifier,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        _connections.Setup(c => c.ForServiceAsync(Identifier, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Failure(new Error("vault", ErrorSeverity.Error, "no key"), State.Error));

        var results = await RunAsync();

        var result = Assert.Single(results);
        Assert.Equal(AuditStatus.CheckFailed, result.Status);
        Assert.Equal(new Uri("https://nexus.example:8443/api/heappe/heappe-1/"), result.HeappeUri);
    }

    [Fact(DisplayName = "Yields ValidityMismatch with expected and actual dates when validity differs")]
    public async Task Should_Yield_ValidityMismatch_With_Expected_And_Actual_When_Dates_Differ()
    {
        SetupTargets(Target(
            start: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            end: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        SetupCredentialsOk();
        SetupHeappeProject("RES-1", HeappeProject(new DateTime(2026, 1, 1), new DateTime(2026, 11, 30)));

        var result = Assert.Single(await RunAsync().ConfigureAwait(false));

        Assert.Equal(AuditStatus.ValidityMismatch, result.Status);
        Assert.True(result.IsFinding);
        Assert.Equal(new DateOnly(2026, 12, 31), result.ExpectedEnd);
        Assert.Equal(new DateOnly(2026, 11, 30), result.ActualEnd);
    }

    [Fact(DisplayName = "Yields Ok when validity differs only in time of day (whole-day, UTC compare)")]
    public async Task Should_Yield_Ok_When_Only_Time_Of_Day_Differs()
    {
        SetupTargets(Target(
            start: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            end: new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero)));
        SetupCredentialsOk();
        // Same calendar days, different times — must not be flagged.
        SetupHeappeProject("RES-1", HeappeProject(new DateTime(2026, 1, 1, 9, 15, 0), new DateTime(2026, 12, 31, 23, 59, 59)));

        var result = Assert.Single(await RunAsync().ConfigureAwait(false));

        Assert.Equal(AuditStatus.Ok, result.Status);
    }

    [Fact(DisplayName = "Yields Missing when the HEAppE instance has no project for the resource")]
    public async Task Should_Yield_Missing_When_Project_Not_Found()
    {
        SetupTargets(Target());
        SetupCredentialsOk();
        _heappe.Setup(h => h.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "RES-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<ProjectModel, Error>.Failure(SomeError("not here"), State.NotFound));

        var result = Assert.Single(await RunAsync().ConfigureAwait(false));

        Assert.Equal(AuditStatus.Missing, result.Status);
        Assert.Null(result.ActualStart);
    }

    [Fact(DisplayName = "Yields CheckFailed with reason when credentials cannot be resolved")]
    public async Task Should_Yield_CheckFailed_When_Credentials_Fail()
    {
        SetupTargets(Target());
        _connections.Setup(c => c.ForServiceAsync(Identifier, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Failure(SomeError("vault down"), State.Error));

        var result = Assert.Single(await RunAsync().ConfigureAwait(false));

        Assert.Equal(AuditStatus.CheckFailed, result.Status);
        Assert.Contains("vault down", result.Reason);
        // The HEAppE instance is never contacted when credentials cannot be resolved.
        _heappe.Verify(h => h.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Yields CheckFailed when the HEAppE lookup errors for a reason other than NotFound")]
    public async Task Should_Yield_CheckFailed_When_Heappe_Errors()
    {
        SetupTargets(Target());
        SetupCredentialsOk();
        _heappe.Setup(h => h.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "RES-1", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result<ProjectModel, Error>.Failure(SomeError("502 bad gateway"), State.Error));

        var result = Assert.Single(await RunAsync().ConfigureAwait(false));

        Assert.Equal(AuditStatus.CheckFailed, result.Status);
    }

    [Fact(DisplayName = "Checks the same resource on the same instance only once")]
    public async Task Should_Deduplicate_Same_Resource_On_Same_Instance()
    {
        SetupTargets(Target(), Target()); // two locations resolving to the same (resource, instance)
        SetupCredentialsOk();
        SetupHeappeProject("RES-1", HeappeProject(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31)));

        var results = await RunAsync();

        Assert.Single(results);
        _heappe.Verify(h => h.GetProjectByResourceName(It.IsAny<BaseHeappeConnectionInfo>(), "RES-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Error SomeError(string description) =>
        new ErrorBuilder().WithDescription(description).WithSeverity(ErrorSeverity.Error).Build();
}
