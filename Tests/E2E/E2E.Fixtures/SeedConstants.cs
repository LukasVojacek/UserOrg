namespace E2E.Fixtures;

/// <summary>
/// Canonical identifiers seeded by <c>seed-data.sql</c> and the <see cref="GatewayAttachFixture"/>
/// bootstrap. Extracted from the previously inlined per-test-class constants so both
/// <c>EndpointsTests</c> and the cross-stack <c>ParityTests</c> share one authoritative source
/// (ADR-0055 D2).
/// </summary>
public static class SeedConstants
{
    public static readonly Guid SeedProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // The 5 regressed-list entities (single seeded row, historical baseline)
    public static readonly Guid ProviderId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    public static readonly Guid LocationId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    public static readonly Guid ProjectTagId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid ProjectResourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid ProjectResourceAssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid SeedRequestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid RuntimeRequestId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    public static readonly Guid PublicMutationRequestId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    // Second rows added for list-ordering parity (ADR-0055): every regressed list endpoint must have
    // ≥2 elements so a broken ORDER BY is observable. Filtered-by-id assertions in EndpointsTests are
    // unaffected; the parity suite (B05) asserts the ordering key across both rows.
    public static readonly Guid SecondProviderId = Guid.Parse("66666666-6666-6666-6666-666666666667");
    public static readonly Guid SecondLocationId = Guid.Parse("88888888-8888-8888-8888-888888888889");
    public static readonly Guid SecondProjectResourceId = Guid.Parse("22222222-2222-2222-2222-222222222223");
    public static readonly Guid SecondRequestId = Guid.Parse("44444444-4444-4444-4444-444444444445");
}
