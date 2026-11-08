using Microsoft.Extensions.Options;

namespace Core.Features.Request.Notifications;

/// <summary>
/// Default <see cref="IActionLinkGenerator"/>: mints two opaque tokens (accept + reject) valid for the
/// configured duration and attaches them to the request aggregate. The token (not a full URL) is stored,
/// so emailed/links resolve through the v2 <c>Public/ActionLinks/{token}</c> route regardless of host.
/// </summary>
internal sealed class ActionLinkGenerator(IOptions<ActionLinkOptions> options) : IActionLinkGenerator
{
    private readonly ActionLinkOptions _options = options.Value;

    public void Generate(Domain.Request.Request request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validTo = DateTimeOffset.UtcNow.Add(_options.Duration);
        request.AddActionLink(ActionLinkAction.Accept, NewToken(), validTo);
        request.AddActionLink(ActionLinkAction.Reject, NewToken(), validTo);
    }

    private static string NewToken() => Guid.NewGuid().ToString("N");
}