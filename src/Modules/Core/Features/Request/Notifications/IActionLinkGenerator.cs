namespace Core.Features.Request.Notifications;

/// <summary>
/// Mints the single-use accept/reject action links for a request, adding them to the aggregate so they
/// are persisted with the same <c>SaveChanges</c>. The tokens are resolved back by the public handle
/// endpoint; <see cref="GetPublicRequestSummaryAsync"/> surfaces them to an approver UI.
/// </summary>
internal interface IActionLinkGenerator
{
    void Generate(Domain.Request.Request request);
}