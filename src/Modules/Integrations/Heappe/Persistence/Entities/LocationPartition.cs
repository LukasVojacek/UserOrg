using UserOrg.Shared.Persistance.Abstractions.BaseEntities;

namespace Heappe.Persistence.Entities;

internal class LocationPartition : AuditableEntity<Guid>
{
    // ponytail: Id generated here (not by the DB default NewId()) so it is known before SaveChangesAsync.
    internal LocationPartition() => Id = Guid.CreateVersion7();

    internal string Name { get; set; }
    internal string? Description { get; set; }
    internal string? AggregationName { get; set; }
    internal Guid LocationId { get; set; }
    internal IList<ProjectCommandTemplatePartition> ProjectCommandTemplatePartitions { get; init; } = [];
}