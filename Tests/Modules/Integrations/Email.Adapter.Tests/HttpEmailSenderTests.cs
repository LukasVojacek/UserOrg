using System.Net;
using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Userorg.Adapter.Email;

using UserOrg.Shared.Email;

namespace Email.Adapter.Tests;

/// <summary>
/// Pins <see cref="HttpEmailSender"/>'s wire contract (target URI, JSON payload shape) and its
/// best-effort failure policy: a delivery failure is swallowed and logged, a cancelled send still
/// throws <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class HttpEmailSenderTests : IDisposable
{
    private const string SenderEmail = "noreply@example.com";
    private const string ExpectedSubject = "Project Assignment Request";

    private HttpClient? _client;

    public void Dispose() => _client?.Dispose();

    [Fact(DisplayName = "SendAsync posts the exact wire payload shape to api/Email/Html")]
    public async Task Should_post_exact_payload_shape()
    {
        var (sender, handler) = CreateSender();
        var message = new EmailMessage(
            TemplateName: "RequestState",
            Receivers: ["user@example.com"],
            TemplateArguments: ["arg-1", "arg-2"],
            Cc: ["cc@example.com"],
            Priority: 1);

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.EndsWith("api/Email/Html", handler.LastRequestUri!.ToString(), StringComparison.Ordinal);
        var payload = handler.LastPayload();
        Assert.Equal(SenderEmail, payload.Sender);
        Assert.Equal(ExpectedSubject, payload.Subject);
        Assert.Equal(["user@example.com"], payload.Receivers);
        Assert.Equal("RequestState", payload.TemplateName);
        Assert.Equal(["cc@example.com"], payload.CC!);
        Assert.Equal(["arg-1", "arg-2"], payload.TemplateArguments);
        Assert.Equal(1, payload.Priority);
    }

    [Fact(DisplayName = "SendAsync omits CC when the message carries none")]
    public async Task Should_omit_cc_When_message_has_no_cc()
    {
        var (sender, handler) = CreateSender();
        var message = new EmailMessage("RequestState", ["user@example.com"], ["arg-1"]);

        await sender.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Null(handler.LastPayload().CC);
    }

    [Fact(DisplayName = "SendAsync swallows a non-cancellation transport failure")]
    public async Task Should_swallow_When_transport_throws()
    {
        using var handler = new ThrowingHandler(new HttpRequestException("boom"));
        var sender = CreateSenderWithHandler(handler);
        var message = new EmailMessage("RequestState", ["user@example.com"], ["arg-1"]);

        var exception = await Record.ExceptionAsync(() => sender.SendAsync(message, CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "SendAsync rethrows OperationCanceledException")]
    public async Task Should_rethrow_When_cancelled()
    {
        using var handler = new ThrowingHandler(new OperationCanceledException());
        var sender = CreateSenderWithHandler(handler);
        var message = new EmailMessage("RequestState", ["user@example.com"], ["arg-1"]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sender.SendAsync(message, CancellationToken.None));
    }

    private (HttpEmailSender Sender, CapturingHandler Handler) CreateSender()
    {
        var handler = new CapturingHandler();
        return (CreateSenderWithHandler(handler), handler);
    }

    private HttpEmailSender CreateSenderWithHandler(HttpMessageHandler handler)
    {
        _client?.Dispose();
        _client = new HttpClient(handler) { BaseAddress = new Uri("http://mail.local/") };
        // Setup keys on the literal, not HttpEmailSender.HttpClientName, so the test actually guards the
        // client name production code requests — renaming the const to the legacy "MailSender" collision
        // would make CreateClient(...) return Mock's default null and fail every test below.
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("UserOrgEmailSender")).Returns(_client);

        var options = Options.Create(new EmailSenderOptions { SenderEmail = SenderEmail });

        return new HttpEmailSender(factory.Object, options, NullLogger<HttpEmailSender>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        private string? _body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            _body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        public EmailPayload LastPayload()
        {
            using var document = JsonDocument.Parse(_body!);
            var root = document.RootElement;
            return new EmailPayload(
                root.GetProperty("Sender").GetString(),
                root.GetProperty("Subject").GetString()!,
                ToStrings(root.GetProperty("Receivers")),
                root.GetProperty("TemplateName").GetString()!,
                root.GetProperty("CC").ValueKind == JsonValueKind.Null ? null : ToStrings(root.GetProperty("CC")),
                ToStrings(root.GetProperty("TemplateArguments")),
                root.GetProperty("Priority").GetInt32());
        }

        private static string[] ToStrings(JsonElement array)
        {
            var values = new string[array.GetArrayLength()];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = array[i].GetString()!;
            }

            return values;
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw exception;
    }

    private sealed record EmailPayload(
        string? Sender, string Subject, string[] Receivers, string TemplateName, string[]? CC, string[] TemplateArguments, int Priority);
}
