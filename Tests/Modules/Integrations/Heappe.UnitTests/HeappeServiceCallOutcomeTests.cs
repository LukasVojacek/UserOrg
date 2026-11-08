using System.Net;

using Heappe.Contracts.CommandTemplate;
using Heappe.Contracts.Connection;
using Heappe.Features.KillSwitch;
using Heappe.Features.VersionResolver;
using Heappe.Helpers.Connection;
using Heappe.Persistence;
using Heappe.UnitTests.Features.Nexus;

using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

using Moq;

using UserOrg.Shared;

using Xunit;

namespace Heappe.UnitTests;

/// <summary>
/// What a HEAppE call through the Nexus proxy hands back to its caller: the answer on success, a failed result when
/// HEAppE refuses or something unexpected breaks the call, and the caller's own cancellation as an exception rather
/// than a failed result. The HTTP boundary is a fake endpoint; <see cref="HeappeService"/> is the real one.
/// </summary>
public sealed class HeappeServiceCallOutcomeTests
{
    private const string Heappe = "/api/heappe/heappe-mock/heappe";
    private const string ListAvailableClustersPath = Heappe + "/ClusterInformation/ListAvailableClusters";
    private const string ProjectsByAccountingStringsPath = Heappe + "/Management/ProjectsByAccountingStrings";
    private const string ChangedFilesPath = Heappe + "/FileTransfer/ListChangedFilesForJob";
    private const string DownloadFilePath = Heappe + "/FileTransfer/DownloadFileFromCluster";
    private const string CommandTemplatePath = Heappe + "/Management/CommandTemplate";
    private const long JobId = 7;

    private const string ChangedFiles = """
        [
          { "FileName": "job.log", "LastModifiedDate": "2026-10-13T12:00:00Z" },
          { "FileName": " ", "LastModifiedDate": "2026-10-13T12:00:00Z" }
        ]
        """;

    private static readonly CommandTemplateUpdateModel TemplateUpdate =
        new(11, "template", null, null, "run.sh", null, 3, IsEnabled: true, SessionCode: null);

    [Fact]
    public async Task GetListAvailableClusters_returns_the_clusters_asked_for_with_the_callers_query()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ListAvailableClustersPath, _ => FakeHttpEndpoint.Json("""[{ "Id": 1, "Name": "Karolina" }]"""));

        var result = await Service(heappe).GetListAvailableClusters(
            Connection(), [new("ProjectId", "5")], TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("Karolina", Assert.Single(result.Value).Name);
        var query = Assert.Single(heappe.RequestUris).Query;
        Assert.Contains("ForceRefresh=true", query, StringComparison.Ordinal);
        Assert.Contains("ProjectId=5", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetListAvailableClusters_returns_a_failure_when_the_call_breaks_unexpectedly()
    {
        using var heappe = Throwing(ListAvailableClustersPath);

        var result = await Service(heappe).GetListAvailableClusters(Connection(), null!, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task GetListAvailableClusters_lets_the_callers_cancellation_through()
    {
        using var heappe = new FakeHttpEndpoint();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(heappe).GetListAvailableClusters(Connection(), null!, Cancelled()).AsTask());
    }

    [Fact]
    public async Task GetProjectByResourceName_returns_the_first_project_with_the_accounting_string()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("""[{ "Id": 42, "AccountingString": "RES-1" }]"""));

        var result = await Service(heappe).GetProjectByResourceName(Connection(), "RES-1", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value.Id);
        Assert.Contains("AccountingString=RES-1", Assert.Single(heappe.RequestUris).Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetProjectByResourceName_reports_not_found_when_no_project_has_the_accounting_string()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("[]"));

        var result = await Service(heappe).GetProjectByResourceName(Connection(), "RES-1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.NotFound, result.State);
    }

    [Fact]
    public async Task GetProjectByResourceName_passes_on_the_failure_heappe_answers()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ProjectsByAccountingStringsPath, _ => FakeHttpEndpoint.Json("boom", HttpStatusCode.InternalServerError));

        var result = await Service(heappe).GetProjectByResourceName(Connection(), "RES-1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal("upstream-error", result.Error.Code);
    }

    [Fact]
    public async Task GetProjectByResourceName_returns_a_failure_when_the_call_breaks_unexpectedly()
    {
        using var heappe = Throwing(ProjectsByAccountingStringsPath);

        var result = await Service(heappe).GetProjectByResourceName(Connection(), "RES-1", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task GetProjectByResourceName_lets_the_callers_cancellation_through()
    {
        using var heappe = new FakeHttpEndpoint();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(heappe).GetProjectByResourceName(Connection(), "RES-1", Cancelled()).AsTask());
    }

    [Fact]
    public async Task DownloadChangedFilesAsync_decodes_every_named_changed_file()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ChangedFilesPath, _ => FakeHttpEndpoint.Json(ChangedFiles))
            .On(HttpMethod.Post, DownloadFilePath, _ => FakeHttpEndpoint.Json("\"aGVs\\nbG8=\""));

        var result = await Service(heappe).DownloadChangedFilesAsync(Connection(), JobId, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var file = Assert.Single(result.Value);
        Assert.Equal("job.log", file.Key);
        Assert.Equal("hello", file.Value.TextContent);
        Assert.Equal(1, heappe.CallsTo(DownloadFilePath));
    }

    [Fact]
    public async Task DownloadChangedFilesAsync_fails_when_heappe_refuses_a_download()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ChangedFilesPath, _ => FakeHttpEndpoint.Json(ChangedFiles))
            .On(HttpMethod.Post, DownloadFilePath, _ => FakeHttpEndpoint.Json("boom", HttpStatusCode.InternalServerError));

        var result = await Service(heappe).DownloadChangedFilesAsync(Connection(), JobId, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task DownloadChangedFilesAsync_fails_when_a_download_is_not_base64()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ChangedFilesPath, _ => FakeHttpEndpoint.Json(ChangedFiles))
            .On(HttpMethod.Post, DownloadFilePath, _ => FakeHttpEndpoint.Json("\"not base64!\""));

        var result = await Service(heappe).DownloadChangedFilesAsync(Connection(), JobId, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task DownloadChangedFilesAsync_lets_a_cancellation_during_the_download_through()
    {
        using var cancellation = new CancellationTokenSource();
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Get, ChangedFilesPath, _ => FakeHttpEndpoint.Json(ChangedFiles))
            .On(HttpMethod.Post, DownloadFilePath, async _ =>
            {
                await cancellation.CancelAsync();
                throw new OperationCanceledException(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(heappe).DownloadChangedFilesAsync(Connection(), JobId, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task ModifyComandTemplateAsync_reports_success_when_heappe_accepts_the_change()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Put, CommandTemplatePath, _ => FakeHttpEndpoint.Json("""{ "Id": 11 }"""));

        var result = await Service(heappe).ModifyComandTemplateAsync(Connection(), TemplateUpdate, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task ModifyComandTemplateAsync_fails_when_heappe_refuses_the_change()
    {
        using var heappe = new FakeHttpEndpoint()
            .On(HttpMethod.Put, CommandTemplatePath, _ => FakeHttpEndpoint.Json("boom", HttpStatusCode.InternalServerError));

        var result = await Service(heappe).ModifyComandTemplateAsync(Connection(), TemplateUpdate, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task ModifyComandTemplateAsync_returns_a_failure_when_the_call_breaks_unexpectedly()
    {
        using var heappe = Throwing(CommandTemplatePath, HttpMethod.Put);

        var result = await Service(heappe).ModifyComandTemplateAsync(Connection(), TemplateUpdate, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(State.Error, result.State);
    }

    [Fact]
    public async Task ModifyComandTemplateAsync_lets_the_callers_cancellation_through()
    {
        using var heappe = new FakeHttpEndpoint();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(heappe).ModifyComandTemplateAsync(Connection(), TemplateUpdate, Cancelled()).AsTask());
    }

    private static ServiceTokenHeappeConnectionInfo Connection() =>
        new(NexusRegistryFake.HeappeMockProxyBase, new HeappeIdentifier("heappe-mock"), "service-token");

    private static CancellationToken Cancelled() => new(canceled: true);

    // An exception HeappeService does not classify as a transport failure, so only the method's own catch sees it.
    private static FakeHttpEndpoint Throwing(string path, HttpMethod? method = null) =>
        new FakeHttpEndpoint().On(
            method ?? HttpMethod.Get,
            path,
            _ => Task.FromException<HttpResponseMessage>(new InvalidOperationException("unexpected")));

    private static HeappeService Service(FakeHttpEndpoint heappe) =>
        new(
            NullLogger<HeappeService>.Instance,
            new FakeHttpClientFactory(heappe),
            new Mock<IFeatureManager>().Object,
            new Mock<IDbContextFactory<HeappeIntegrationDbContext>>().Object,
            new Mock<IKillSwitchStateService>().Object,
            new Mock<IHeappeVersionResolver>().Object,
            Options.Create(new ClusterAuthTypeOptions()));
}
