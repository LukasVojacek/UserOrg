namespace Core.DataAccess.Queries.Contracts;

/// <summary>A project using a resource, paired with that resource's name — one HEAppE sync <c>WorkItem</c>.</summary>
public sealed record ResourceProjectRef(ProjectShortName ProjectShortName, ResourceName ResourceName);