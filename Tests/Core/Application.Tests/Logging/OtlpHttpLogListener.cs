using System.Collections.Concurrent;
using System.Net;

using Google.Protobuf;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace UserOrganizationManagement.Application.Tests.Logging;

internal sealed class OtlpHttpLogListener : IAsyncDisposable
{
    private const int ResourceLogsField = 1;
    private const int ScopeLogsField = 2;
    private const int LogRecordsField = 2;
    private const int BodyField = 5;
    private const int StringValueField = 1;

    private readonly WebApplication _app;
    private readonly ConcurrentQueue<string> _logBodies = new();

    private OtlpHttpLogListener(WebApplication app) => _app = app;

    public string Endpoint => _app.Urls.Single();

    public IReadOnlyCollection<string> LogBodies => _logBodies;

    public static async Task<OtlpHttpLogListener> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));

        var listener = new OtlpHttpLogListener(builder.Build());
        listener._app.MapPost("/v1/logs", listener.ReceiveLogsAsync);
        listener._app.MapPost("/{**otherSignal}", static () => TypedResults.Ok());
        await listener._app.StartAsync();
        return listener;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task<IResult> ReceiveLogsAsync(HttpRequest request)
    {
        var exportRequest = await ByteString.FromStreamAsync(request.Body, request.HttpContext.RequestAborted);

        foreach (var body in LogRecordBodies(exportRequest))
        {
            _logBodies.Enqueue(body);
        }

        return TypedResults.Ok();
    }

    private static IEnumerable<string> LogRecordBodies(ByteString exportRequest) =>
        from resourceLogs in LengthDelimitedFields(exportRequest, ResourceLogsField)
        from scopeLogs in LengthDelimitedFields(resourceLogs, ScopeLogsField)
        from logRecord in LengthDelimitedFields(scopeLogs, LogRecordsField)
        from body in LengthDelimitedFields(logRecord, BodyField)
        from text in LengthDelimitedFields(body, StringValueField)
        select text.ToStringUtf8();

    private static IEnumerable<ByteString> LengthDelimitedFields(ByteString message, int fieldNumber)
    {
        using var input = message.CreateCodedInput();

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            if (WireFormat.GetTagFieldNumber(tag) == fieldNumber
                && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                yield return input.ReadBytes();
            }
            else
            {
                input.SkipLastField();
            }
        }
    }
}