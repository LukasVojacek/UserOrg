namespace Waldur.Sync.Mapping;

/// <summary>The enumerated skip reasons, one per today's inline/aggregate/ensure validation rule.</summary>
public enum EMappingSkipReason
{
    ProviderIdMissing,
    ProviderNameMissing,
    ProviderCountryMissing,
    ProviderNoLocations,

    LocationInvalidNoSshEndpoint,
    LocationMissingFields,
    LocationNameMissing,
    LocationSlugMissing,
    LocationStateMissing,
    LocationDifferentType,
    LocationTypeMissing,

    ProjectIdMissing,
    ProjectNameMissing,
    ProjectSlugInvalidOrMissing,
    ProjectCreatedMissing,
    ProjectNoEligibleResources,

    ResourceIdentifierMissing,
    ResourceNoAllocations,
    ResourceNoBackendOrEffectiveId,
    ResourceNameMissing,
    ResourceStateMissing,
    ResourceMappingMissing,
    ResourceOfferingMissing,
    ResourceOfferingNameMissing,
    ResourceOfferingStateMissing,
    ResourceNoSshEndpoint,
    ResourceReconciliationOwned,

    MemberUserIdentifierMissing,
    MemberUserNameMissing,
    MemberUserEmailMissing,
    MemberUnknownRole,
}