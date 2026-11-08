using Lukdrasil.StepUpLogging;

namespace Core.Shared.Auth;

/// <summary>
/// Composes an <see cref="AuditEvent"/> from a <see cref="Caller"/>. <see cref="AuditEvent.Data"/>
/// carries identifiers only (here, the raw <c>sub</c> claim) - never <see cref="Caller.Email"/> or
/// <see cref="Caller.Name"/>: audit events land in append-only immudb, where a GDPR erasure request
/// cannot be honoured, so the link to a person must stay in the userorg DB, which can be erased.
/// </summary>
/// <remarks>
/// Call sites that need their own <c>Data</c> entries pass them as <c>data</c> and get them merged with
/// the mapper-owned ones, rather than building <see cref="AuditEvent.Data"/> by hand and bypassing this
/// containment. <c>sub</c> is mapper-owned: supplying it throws <see cref="ArgumentException"/> instead of
/// being silently overwritten, so an attempt to restate caller identity surfaces as the bug it is.
/// The mapper cannot inspect caller-supplied values - keeping personal data out of them is the call
/// site's responsibility.
/// </remarks>
internal static class CallerAuditMapper
{
    private const string SubKey = "sub";

    private const string UserActorType = "user";

    private const string ServiceActorType = "service";

    internal static AuditEvent ComposeSuccess(
        string action,
        Caller caller,
        ProjectShortName? tenantId = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        Compose(AuditEvent.Success(action, caller.IdpId.Value, ResolveActorType(caller)), caller, tenantId, data);

    internal static AuditEvent ComposeDenied(
        string action,
        Caller caller,
        ProjectShortName? tenantId = null,
        IReadOnlyDictionary<string, object?>? data = null) =>
        Compose(AuditEvent.Denied(action, caller.IdpId.Value, ResolveActorType(caller)), caller, tenantId, data);

    // The same discriminator the Gateway audits on (AuthorizationHelpers.ResolveActorType), so both sides
    // of a request describe the caller identically. The Gateway's third value, "anonymous", cannot occur
    // here: a Caller only binds once a 'sub' claim resolves.
    // IsServiceKeyIdentity is false-by-default: it means "not proven to be a service key" (an absent or
    // malformed 'am' claim reads as false), not "proven to be a user". Falling back to "user" is only safe
    // because every request arrives through the Gateway, which mints 'am' via EAuthorizationMethod.FromString
    // (InternalTokenTransformExtensions) and throws on unknown or empty input. An ingress that bypasses that
    // - direct Core access, a new dev-auth handler - would silently stamp machines as "user" in append-only
    // storage, so it must establish the actor kind itself before reaching here.
    private static string ResolveActorType(Caller caller) =>
        caller.IsServiceKeyIdentity ? ServiceActorType : UserActorType;

    private static AuditEvent Compose(
        AuditEvent auditEvent,
        Caller caller,
        ProjectShortName? tenantId,
        IReadOnlyDictionary<string, object?>? data)
    {
        if (data?.ContainsKey(SubKey) == true)
            throw new ArgumentException($"'{SubKey}' is owned by the mapper and cannot be supplied by a caller.", nameof(data));

        Dictionary<string, object?> composedData = data is null ? [] : new(data);
        composedData[SubKey] = caller.RawSub;

        return auditEvent with
        {
            TenantId = tenantId?.Value,
            Data = composedData,
        };
    }
}