using Core.Domain.OnboardingRequest;

using Microsoft.EntityFrameworkCore;

namespace Core.DataAccess.Contexts;

/// <summary>
/// Write adapter for the <see cref="OnboardingRequest"/> aggregate, keyed by its natural public id
/// <see cref="OnboardingRequestSlug"/>: create Adds a new aggregate, accept/reject load it by slug.
/// The aggregate carries no surrogate id or FK guids (ADR-0046), so on add this adapter assigns the
/// shadow <c>Id</c> a Guid v7 (mirroring <c>DatabaseContext.ProjectTagHandler</c>) and resolves the
/// <c>ProjectId</c>/<c>UserId</c> shadow FKs from the aggregate's natural keys via the directory ports.
/// </summary>
internal sealed partial class DatabaseContext : IRepository<OnboardingRequest, OnboardingRequestSlug>
{
    async ValueTask<OnboardingRequest?> IRepository<OnboardingRequest, OnboardingRequestSlug>.AddAsync(OnboardingRequest aggregate, CancellationToken cancellationToken)
    {
        var projectId = await Projects
            .Where(p => p.ShortName == aggregate.Project)
            .Select(p => (ProjectId?)EF.Property<ProjectId>(p, "Id"))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Project '{aggregate.Project.Value}' not found while creating an onboarding request.");

        var userId = await Users
            .Where(u => u.IdentityProviderId == aggregate.Requester)
            .Select(u => (UserId?)EF.Property<UserId>(u, "Id"))
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"User '{aggregate.Requester.Value}' not found while creating an onboarding request.");

        var entry = await OnboardingRequests.AddAsync(aggregate, cancellationToken);
        entry.Property("Id").CurrentValue = Guid.CreateVersion7();
        entry.Property("ProjectId").CurrentValue = projectId;
        entry.Property("UserId").CurrentValue = userId;
        RegisterAddedAggregateRoot(aggregate);
        return aggregate;
    }

    void IRepository<OnboardingRequest, OnboardingRequestSlug>.Delete(OnboardingRequest aggregate, CancellationToken cancellationToken) => OnboardingRequests.Remove(aggregate);

    async ValueTask<OnboardingRequest?> IRepository<OnboardingRequest, OnboardingRequestSlug>.FindAsync(OnboardingRequestSlug key, CancellationToken cancellationToken) => await OnboardingRequests
        .FirstOrDefaultAsync(r => r.Slug == key, cancellationToken);
}