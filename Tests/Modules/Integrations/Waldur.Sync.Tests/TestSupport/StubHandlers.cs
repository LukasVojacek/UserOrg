using System.Net;

namespace Waldur.Sync.Tests.TestSupport;

/// <summary>Returns a fixed status/body per call, walking a sequence (last entry repeats).</summary>
internal sealed class SequenceHandler : HttpMessageHandler
{
    private readonly (HttpStatusCode Status, string Body)[] _steps;

    public SequenceHandler(params (HttpStatusCode Status, string Body)[] steps) => _steps = steps;

    public int CallCount { get; private set; }

    public List<Uri?> RequestedUris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestedUris.Add(request.RequestUri);
        var step = _steps[Math.Min(CallCount, _steps.Length - 1)];
        CallCount++;
        return Task.FromResult(new HttpResponseMessage(step.Status)
        {
            Content = new StringContent(step.Body)
        });
    }
}
