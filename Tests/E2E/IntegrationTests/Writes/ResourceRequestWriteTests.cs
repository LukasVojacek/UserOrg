using System.Net.Http.Json;
using System.Text.Json;

using E2E.Fixtures;

using IntegrationTests.Support;

using Microsoft.Data.SqlClient;

using ParityTests;

namespace IntegrationTests.Writes;

/// <summary>
/// Async v2 write scenario (ADR-0063/0064/0067): the resource-assignment request accept and reject flows,
/// driven end-to-end through the Gateway. Creating a request dispatches the <c>AssignResourceFlow</c> to the
/// background <c>RequestWorker</c>; the flow's start step mints a single-use PI action-link token and emails it,
/// and the PI's decision is delivered back through the anonymous token endpoint, resuming the <c>PiApproval</c>
/// gate. Because every transition runs on the worker (not the HTTP thread), the terminal assertion is a bounded
/// poll of the projection row — never a fixed sleep.
/// </summary>
/// <remarks>
/// Scope note: this v2 flow (ADR-0063) does <b>not</b> run an <c>InitializeHeappeStep</c> — the accept path
/// attaches the resource through the F5 write path (<c>Project.AssignResource</c>) and lets HEAppE sync ride the
/// existing durable outbox event; there is no synchronous, deterministically-observable HEAppE init call inside
/// the request workflow. The worker-driven, deterministically-observable side effect this suite asserts on is the
/// emailed PI action-link (captured from the EmailSender WireMock) plus the terminal projection state. The token
/// capture and email polling are the reusable seam B05 builds its EmailSender assertions on.
/// </remarks>
[Collection(ParityTestCollection.Name)]
public sealed class ResourceRequestWriteTests(GatewayAttachFixture fixture, TestcontainersTopologyFixture topology)
{
    private const string ProjectShortName = "TESTPRJ";
    private const string ProviderName = "prov1";
    private const string AcceptResourceName = "reqaccept";
    private const string RejectResourceName = "reqreject";
    private const string EmailOkResourceName = "reqemailok";
    private const string Email500ResourceName = "reqemail500";
    private const string NoMatchingResourceName = "reqnomatch";

    // The worker settles off the HTTP thread; a generous but hard-bounded window absorbs first-touch JIT and the
    // poll cadence without ever waiting on a fixed sleep for the assertion.
    private static readonly TimeSpan TerminalTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Should_ReachAssigned_When_PiApprovesRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreateResourceRequestAsync(owner, AcceptResourceName, ct);
        var token = await PollForActionTokenAsync(slug, ct);
        await SettleAsync(owner, token, "Approve", ct);

        var state = await PollForTerminalStateAsync(slug, ct);
        Assert.Equal("Assigned", state);
    }

    [Fact]
    public async Task Should_ReachRejected_When_PiRejectsRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreateResourceRequestAsync(owner, RejectResourceName, ct);
        var token = await PollForActionTokenAsync(slug, ct);
        await SettleAsync(owner, token, "Reject", ct);

        var state = await PollForTerminalStateAsync(slug, ct);
        Assert.Equal("Rejected", state);
    }

    // DEC-1/criterion 2: ResourceName is free text and need not name an existing ProjectResource row. This
    // resource name has no seeded row at all, so the create-through-approve path is the end-to-end proof that
    // the request survives rather than being rejected for an unresolvable resource (B01's unit test only proves
    // the endpoint stopped looking the resource up). DEC-3 (Variant B) then parks it on the second gate.
    [Fact]
    public async Task Should_ParkAtAwaitingResourceCreation_When_ResourceNameHasNoMatchingRow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreateResourceRequestAsync(owner, NoMatchingResourceName, ct);
        var token = await PollForActionTokenAsync(slug, ct);
        await SettleAsync(owner, token, "Approve", ct);

        var state = await PollUntilTerminal.ForAsync(
            probeCt => QueryRequestStateAsync(slug, probeCt),
            candidate => candidate == "AwaitingResourceCreation",
            TerminalTimeout,
            PollInterval,
            $"'AwaitingResourceCreation' state for request '{slug}'",
            ct);

        Assert.Equal("AwaitingResourceCreation", state);
    }

    // Verified from code (Core.Features.Request.Notifications.MailNotificationSender.SendAsync): the send never
    // calls EnsureSuccessStatusCode and catches every exception except OperationCanceledException, only logging
    // it — so a mail-service failure (transport error or, as here, a mocked non-2xx status) never propagates to
    // the caller. The flow's step handlers (SendPiApprovalEmailHandler, NotifyRequesterAssignedHandler,
    // NotifyRequesterRejectedHandler) wrap the send in FlowHandlerHelpers.SendOrRetryAsync, but that retry path is
    // unreachable for an HTTP-level failure like this test's 500 — only a thrown transport exception would trigger
    // it. The projection state is also already committed to its terminal value before the notification step runs
    // (NotifyRequesterRejectedHandler sets State=Rejected before sending; NotifyRequesterAssignedHandler's own
    // doc comment confirms AttachResourceHandler already set Assigned). Net effect: notification is fire-and-log,
    // never a hard dependency of the workflow's terminal state.
    [Fact]
    public async Task Should_RecordEmailSend_When_RequestAccepted()
    {
        var ct = TestContext.Current.CancellationToken;
        using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

        var slug = await CreateResourceRequestAsync(owner, EmailOkResourceName, ct);
        var token = await PollForActionTokenAsync(slug, ct);
        await SettleAsync(owner, token, "Approve", ct);

        Assert.Equal("Assigned", await PollForTerminalStateAsync(slug, ct));

        var recipient = await PollForRequestStateEmailRecipientAsync(slug, "Assigned", ct);
        Assert.Equal("test@test.cz", recipient);
    }

    [Fact]
    public async Task Should_StillReachTerminalState_When_EmailSenderReturns500()
    {
        var ct = TestContext.Current.CancellationToken;
        topology.WireMock.SetEmailSenderStatusCode(500);
        try
        {
            using var owner = await fixture.CreateProjectOwnerClientAsync(ct);

            var slug = await CreateResourceRequestAsync(owner, Email500ResourceName, ct);
            await SettleWithRetryOnSupersededTokenAsync(owner, slug, "Approve", ct);

            Assert.Equal("Assigned", await PollForTerminalStateAsync(slug, ct));
        }
        finally
        {
            topology.WireMock.ResetEmailSender();
        }
    }

    private static async Task<string> CreateResourceRequestAsync(HttpClient owner, string resourceName, CancellationToken ct)
    {
        using var response = await owner.PostAsJsonAsync(
            new Uri($"/api/v2/Projects/{ProjectShortName}/ResourceRequests", UriKind.Relative),
            new { ProviderName, ResourceName = resourceName, PrimaryInvestigatorEmail = "pi@test.cz" },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 202 creating a resource request for '{resourceName}', got {(int)response.StatusCode}.{Environment.NewLine}{body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("Slug").GetString()
            ?? throw new InvalidOperationException("Create response did not carry a Slug.");
    }

    private static async Task SettleAsync(HttpClient client, string token, string decision, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(
            new Uri($"/api/v2/Public/ResourceRequestActions/{token}", UriKind.Relative),
            new { Decision = decision },
            ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        Assert.True(
            response.StatusCode == HttpStatusCode.NoContent,
            $"Expected 204 delivering '{decision}' to the action link, got {(int)response.StatusCode}.{Environment.NewLine}{body}");
    }

    // Under a failing EmailSender, SendPiApprovalEmailHandler's send retries through the standard resilience
    // handler (30s total timeout) and can outlast the workflow engine's step lease; the engine then re-runs the
    // step and mints a token that supersedes the one this test first observed. Settling with a superseded token
    // 404s (ADR-0064 collapses "superseded" into the same anti-leak response as "unknown"), so this polls for
    // the currently-live token and retries the settle call until the engine stops superseding it.
    private async Task SettleWithRetryOnSupersededTokenAsync(HttpClient client, string slug, string decision, CancellationToken ct)
    {
        var settled = await PollUntilTerminal.ForAsync(
            async probeCt =>
            {
                var token = TryExtractActionToken(slug);
                if (token is null)
                {
                    return false;
                }

                using var response = await client.PostAsJsonAsync(
                    new Uri($"/api/v2/Public/ResourceRequestActions/{token}", UriKind.Relative),
                    new { Decision = decision },
                    probeCt);
                return response.StatusCode == HttpStatusCode.NoContent;
            },
            settledOnce => settledOnce,
            TerminalTimeout,
            PollInterval,
            $"settling request '{slug}' via its (possibly superseded) action-link token",
            ct);

        Assert.True(settled, $"Never settled request '{slug}' — its action-link token kept getting superseded.");
    }

    // The plaintext action-link token is emailed and never persisted (only its hash is stored), so it is
    // recovered from the EmailSender WireMock recording — the same worker-driven side effect B05 asserts on.
    // Bounded poll: SendPiApprovalEmail runs asynchronously after the 202.
    private async Task<string> PollForActionTokenAsync(string slug, CancellationToken ct)
    {
        var token = await PollUntilTerminal.ForAsync(
            _ => Task.FromResult(TryExtractActionToken(slug)),
            candidate => candidate is not null,
            TerminalTimeout,
            PollInterval,
            $"PI approval email for request '{slug}'",
            ct);
        return token!;
    }

    private static Task<string?> PollForTerminalStateAsync(string slug, CancellationToken ct) =>
        PollUntilTerminal.ForAsync(
            probeCt => QueryRequestStateAsync(slug, probeCt),
            state => state is not null && IsTerminal(state),
            TerminalTimeout,
            PollInterval,
            $"terminal projection state for request '{slug}'",
            ct);

    // The PI-approval email is the "NewProjectAssignment_PM" template; its second positional argument is the
    // action link "{BaseUrl}/{token}" (ResourceRequestActionLinkOptions.BuildActionLink). Matching on the first
    // argument (the request slug) isolates this request's mail from the accept/reject notification mails.
    //
    // Takes the LAST match, not the first: SendPiApprovalEmailHandler commits a fresh token before every send
    // attempt, and the send goes through the standard resilience handler (AddServiceDefaults, 30s total timeout
    // with retries on non-2xx). A mocked non-2xx EmailSender (B05's 500 scenario) can make one attempt outlast
    // the workflow engine's step lease, so the engine re-runs the step and mints a second token that supersedes
    // the first in storage — only the most recently recorded mail reflects the currently-live token.
    private string? TryExtractActionToken(string slug)
    {
        string? latest = null;

        foreach (var body in topology.WireMock.RecordedEmailRequestBodies())
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("TemplateName", out var template)
                || template.GetString() != "NewProjectAssignment_PM"
                || !root.TryGetProperty("TemplateArguments", out var arguments)
                || arguments.ValueKind != JsonValueKind.Array
                || arguments.GetArrayLength() < 2
                || arguments[0].GetString() != slug)
            {
                continue;
            }

            var link = arguments[1].GetString();
            if (string.IsNullOrEmpty(link))
            {
                continue;
            }

            var lastSegment = link[(link.LastIndexOf('/') + 1)..];
            latest = Uri.UnescapeDataString(lastSegment);
        }

        return latest;
    }

    // The post-decision notification ("RequestState" template) is emailed off the worker after the projection
    // already committed its terminal state — see NotifyRequesterAssignedHandler/NotifyRequesterRejectedHandler.
    // Matching on the slug (arg 0) and the state slot (arg 2) isolates this request's notification.
    private async Task<string> PollForRequestStateEmailRecipientAsync(string slug, string stateSlot, CancellationToken ct)
    {
        var recipient = await PollUntilTerminal.ForAsync(
            _ => Task.FromResult(TryExtractRequestStateEmailRecipient(slug, stateSlot)),
            candidate => candidate is not null,
            TerminalTimeout,
            PollInterval,
            $"'{stateSlot}' notification email for request '{slug}'",
            ct);
        return recipient!;
    }

    private string? TryExtractRequestStateEmailRecipient(string slug, string stateSlot)
    {
        foreach (var body in topology.WireMock.RecordedEmailRequestBodies())
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (!root.TryGetProperty("TemplateName", out var template)
                || template.GetString() != "RequestState"
                || !root.TryGetProperty("TemplateArguments", out var arguments)
                || arguments.ValueKind != JsonValueKind.Array
                || arguments.GetArrayLength() < 3
                || arguments[0].GetString() != slug
                || arguments[2].GetString() != stateSlot
                || !root.TryGetProperty("Receivers", out var receivers)
                || receivers.ValueKind != JsonValueKind.Array
                || receivers.GetArrayLength() < 1)
            {
                continue;
            }

            var recipient = receivers[0].GetString();
            if (!string.IsNullOrEmpty(recipient))
            {
                return recipient;
            }
        }

        return null;
    }

    private static async Task<string?> QueryRequestStateAsync(string slug, CancellationToken ct)
    {
        var connectionString = Environment.GetEnvironmentVariable("PARITY_SQL_CONNECTION")
            ?? throw new InvalidOperationException("PARITY_SQL_CONNECTION is not set — the topology fixture must run first.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT [State] FROM [resourceRequests].[ResourceRequests] WHERE [Slug] = @slug";
        command.Parameters.AddWithValue("@slug", slug);

        return await command.ExecuteScalarAsync(ct) as string;
    }

    private static bool IsTerminal(string state) =>
        state is "Assigned" or "Rejected" or "Cancelled" or "Failed";
}
