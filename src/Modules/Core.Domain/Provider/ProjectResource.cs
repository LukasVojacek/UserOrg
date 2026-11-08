using Core.Domain.Shared;

namespace Core.Domain.Provider;

/// <summary>
/// ProjectResource — aggregate root in the Provider bounded context (ADR-0026 / ADR-0031). Belongs to exactly
/// one Provider, identified by <c>(provider, Name)</c>; the same name may exist under different providers. The
/// owning provider is carried as a <b>shadow FK</b> on <c>Provider.Id</c> (referential integrity in storage);
/// the domain surface exposes only the provider's natural key via <see cref="ProviderName"/>. It is null only
/// for legacy standalone rows — a v2-created resource always carries one. Name and owning provider are identity
/// and are immutable (rename is disabled, ADR-0030). Id, CreatedAt and ModifiedAt are EF shadow properties.
/// </summary>
public sealed class ProjectResource : AggregateRoot, ISoftDeletableEntity, IServiceIntegrationEntity, IHasValidityPeriod
{
    // EF loads the owning provider into this backing field through the shadow FK "ProviderId" (ADR-0031);
    // the surrogate id stays in storage and never crosses a contract. Mirrors Project._projectDomainType.
    private Provider? _provider;

    public ResourceName ResourceName { get; private set; } = default!;

    /// <summary>Owning provider's natural key (ADR-0031). Null only for legacy standalone rows.</summary>
    public ProviderName? ProviderName => _provider?.Name;

    /// <summary>
    /// Cross-aggregate natural identity (ADR-0044 §1): <c>(ProviderName, ResourceName)</c>. Other aggregates
    /// reference this resource by this key, never by the surrogate. Throws for a legacy standalone row whose
    /// owning provider is null — such a row has no per-provider natural key and is never a v2 assignment target.
    /// </summary>
    public ProjectResourceKey Key =>
        new(_provider?.Name ?? throw new InvalidOperationException("Resource has no owning provider; it has no natural key."), ResourceName);

    public string? Description { get; private set; }
    public Email? PrimaryInvestigatorEmail { get; private set; }
    public EOperationMode OperationMode { get; private set; } = EOperationMode.Unspecified;

    public DateTimeOffset StartDate { get; private set; }
    public DateTimeOffset EndDate { get; private set; }
    public ExternalId? ExternalId { get; private set; }
    public bool IsDeleted { get; private set; }
    public ServiceName ServiceName { get; private set; }
    public EProjectResourceState State { get; private set; } = EProjectResourceState.Draft;
    public string? LicenseUrl { get; private set; }
    public UserId? CreatedBy { get; private set; }

    /// <summary>Cross-aggregate reference to the creating <c>User</c>.</summary>
    /// <summary>
    /// Creates a resource that is immediately usable. Raises <see cref="ProjectResourceActivatedEvent"/> so any
    /// assignment request already parked on this resource key resumes.
    /// </summary>
    public static ProjectResource Create(
        ResourceName resourceName,
        Provider provider,
        UserId createdBy,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string? description = null,
        ServiceName? serviceName = null,
        EOperationMode operationMode = EOperationMode.Unspecified,
        Email? primaryInvestigatorEmail = null,
        ExternalId? externalId = null,
        string? licenseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (endDate < startDate)
        {
            throw new ArgumentException("StartDate must not be after EndDate", nameof(startDate));
        }

        var resource = new ProjectResource
        {
            ResourceName = resourceName,
            Description = description,
            PrimaryInvestigatorEmail = primaryInvestigatorEmail,
            OperationMode = operationMode,
            StartDate = startDate,
            EndDate = endDate,
            ExternalId = externalId,
            ServiceName = serviceName ?? ServiceName.Default,
            IsDeleted = false,
            LicenseUrl = licenseUrl,
            CreatedBy = createdBy
        };
        resource._provider = provider;

        // Raised here in the factory — before allocation — deliberately; do not move it "later" (DEC-4).
        // A resource is usable the moment it exists, and this is the ONLY thing that wakes an assignment
        // request parked on the resource-availability gate.
        // Raising it this early is immaterial: Create() and AllocateAll() run in one synchronous block ahead of
        // a single SaveChangesAsync(), and domain events leave the process only through the post-commit outbox.
        // If allocation fails, the aggregate is never added to the repository, nothing commits, and this event
        // is discarded along with it.
        resource.AddDomainEvent(new ProjectResourceActivatedEvent(resource.Key));
        return resource;
    }

    /// <summary>
    /// Updates the mutable metadata. Name, owning provider and service name are identity and stay fixed.
    /// </summary>
    public void UpdateDetails(
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string? description,
        EOperationMode operationMode,
        Email? primaryInvestigatorEmail,
        ExternalId? externalId,
        string? licenseUrl)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("StartDate must not be after EndDate", nameof(startDate));
        }

        StartDate = startDate;
        EndDate = endDate;
        Description = description;
        OperationMode = operationMode;
        PrimaryInvestigatorEmail = primaryInvestigatorEmail;
        ExternalId = externalId;
        LicenseUrl = licenseUrl;
    }

    /// <summary>Updates the resource's publication state.</summary>
    public void UpdateState(EProjectResourceState newState) => State = newState;

    public void Delete() => IsDeleted = true;
}