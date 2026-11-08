using UserOrganizationManagement.Domain.ValueObjects;

namespace UserOrganizationManagement.Domain.Entities;

/// <summary>
/// Legacy persistence shim for the v2 <c>OnboardingRequest</c> aggregate (ADR-0046, ADR-0047).
/// The aggregate itself lives in <c>Core.Domain.OnboardingRequest</c> and the runtime read/write mapping is
/// owned by <c>Core.DataAccess</c>'s <c>OnboardingRequestConfiguration</c>. This type exists only so the
/// <b>physical table</b> can be created through the legacy <c>DatabaseContext</c> migration pipeline (the
/// context the DatabaseMigrations tool + CI already apply); it deliberately mirrors the modular config's
/// column shape exactly and carries no behaviour. It is NOT an <c>IAuditableEntity</c> on purpose, so the
/// global auditable conventions do not inject extra ServiceName/Mode/ExternalId columns the modular config
/// does not have.
/// </summary>
public sealed class OnboardingRequest
{
    public Guid Id { get; set; }

    public ProjectId ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string ContactEmail { get; set; } = null!;

    public int State { get; set; }

    public string? Slug { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ModifiedAt { get; set; }
}