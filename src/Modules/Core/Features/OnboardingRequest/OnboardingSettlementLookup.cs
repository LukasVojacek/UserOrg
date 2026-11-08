using Core.Domain.OnboardingRequest;

using Lukdrasil.StepUpLogging;

using OnboardingRequestAggregate = Core.Domain.OnboardingRequest.OnboardingRequest;

namespace Core.Features.OnboardingRequest;

/// <summary>
/// Resolves a settleable onboarding request for the Accept and Reject endpoints. A genuine miss, an
/// out-of-scope caller, a wrong-project slug, a malformed slug and a raced delete all return null, so the
/// caller answers them with one byte-identical not_found (ADR-0018 rider). Only the out-of-scope denial is
/// audited, under the caller's <paramref name="auditAction"/>.
/// </summary>
internal static class OnboardingSettlementLookup
{
    internal static async Task<(OnboardingRequestSettlement Settlement, OnboardingRequestAggregate Request)?> FindSettleableAsync<TCategory>(
        string slugValue,
        ProjectShortName routeShortName,
        Caller caller,
        string auditAction,
        IOnboardingRequestQuery onboardingRequestQuery,
        IRepository<OnboardingRequestAggregate, OnboardingRequestSlug> onboardingRequests,
        IAuditLogger<TCategory> auditLogger,
        CancellationToken ct)
    {
        var settlement = await onboardingRequestQuery.FindSettlementBySlugAsync(slugValue, ct);
        if (settlement is null)
            return null;

        if (!caller.Scope.Contains(routeShortName))
        {
            await auditLogger.AuditAsync(CallerAuditMapper.ComposeDenied(
                auditAction, caller, routeShortName) with
            {
                TargetType = AuditActions.TargetTypes.OnboardingRequest,
                TargetId = slugValue,
                Reason = AuditActions.DenialReasons.ProjectCallerOutOfScope,
            });
            return null;
        }

        if (settlement.Project != routeShortName)
            return null;

        if (!OnboardingRequestSlug.TryCreate(slugValue, out var slug, out _))
            return null;

        return await onboardingRequests.FindAsync(slug, ct) is { } settledRequest
            ? (settlement, settledRequest)
            : null;
    }
}