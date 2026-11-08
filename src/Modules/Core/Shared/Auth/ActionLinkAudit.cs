using Core.Features.ResourceRequest.Flow;

using Lukdrasil.StepUpLogging;

namespace Core.Shared.Auth;

internal static class ActionLinkAudit
{
    private const string ActorId = "anonymous";
    private const string ActorType = "action-link";
    private const string TokenHashKey = "token_hash";

    internal static AuditEvent Denied(string action, string? token, string reason) =>
        AuditEvent.Denied(action, ActorId, ActorType) with
        {
            Reason = reason,
            Data = TokenData(token),
        };

    internal static AuditEvent Success(
        string action,
        string? token,
        string tenantId,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        var composed = TokenData(token);
        foreach (var (key, value) in data ?? new Dictionary<string, object?>())
            composed.Add(key, value);

        return AuditEvent.Success(action, ActorId, ActorType) with
        {
            TenantId = tenantId,
            Data = composed,
        };
    }

    private static Dictionary<string, object?> TokenData(string? token) =>
        new() { [TokenHashKey] = string.IsNullOrWhiteSpace(token) ? null : ResourceRequestToken.Hash(token) };
}