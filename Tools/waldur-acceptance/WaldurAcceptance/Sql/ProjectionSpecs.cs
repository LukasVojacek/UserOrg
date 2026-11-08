using UserOrg.Tools.WaldurAcceptance.Core;

namespace UserOrg.Tools.WaldurAcceptance.Sql;

/// <summary>
/// The natural-key projections for the UserOrg tables ADR-0059 names: projects, resources /
/// allocations, providers, locations, memberships / permissions / access. Column names verified
/// against the modular Core.DataAccess EF configuration (Project, Provider, ProjectResource whose
/// natural name maps to the <c>Name</c> column, Location, AssignedLocation, AssignedUserToProject,
/// UserPermission, UserProjectAccess). Reconciliation-owned <c>AssignedLocation</c> rows
/// (<c>ServiceName = @EfpCoreServiceName</c>, D14) are excluded, matching the payload-mapping rule.
/// FKs are joined through to external ids / names so surrogate GUIDs never appear.
/// </summary>
public static class ProjectionSpecs
{
    public static IReadOnlyList<ProjectionSpec> All { get; } =
    [
        new ProjectionSpec(
            EntityTypes.Project,
            """
            SELECT p.ExternalId, p.ServiceName, p.Name, p.ShortName, p.Email,
                   p.StartDate, p.EndDate, CAST(p.IsDeleted AS int) AS IsDeleted
            FROM [Project] p
            """,
            ["ExternalId", "ServiceName"]),

        new ProjectionSpec(
            EntityTypes.Provider,
            """
            SELECT pr.ExternalId, pr.ServiceName, pr.Name, pr.Description, pr.SupportEmail,
                   CAST(pr.IsPrivate AS int) AS IsPrivate, c.Name AS CountryName,
                   CAST(pr.IsDeleted AS int) AS IsDeleted
            FROM [Provider] pr
            LEFT JOIN [Country] c ON c.Id = pr.CountryId
            """,
            ["ExternalId", "ServiceName"]),

        // ProjectResource natural key is (Name, ServiceName): ExternalId is null and Name holds the
        // grouped EffectiveId/BackendId. Provider joined through to its ExternalId.
        new ProjectionSpec(
            EntityTypes.ProjectResource,
            """
            SELECT r.Name, r.ServiceName, r.Description, r.PrimaryInvestigatorEmail,
                   r.StartDate, r.EndDate, CAST(r.IsDeleted AS int) AS IsDeleted,
                   pv.ExternalId AS ProviderExternalId
            FROM [ProjectResource] r
            LEFT JOIN [Provider] pv ON pv.Id = r.ProviderId
            """,
            ["Name", "ServiceName"]),

        new ProjectionSpec(
            EntityTypes.Location,
            """
            SELECT l.Endpoint, l.ServiceName, l.Name, CAST(l.IsPrivate AS int) AS IsPrivate,
                   CAST(l.IsShared AS int) AS IsShared, CAST(l.IsDeleted AS int) AS IsDeleted,
                   pv.ExternalId AS ProviderExternalId, lt.Name AS LocationType
            FROM [Location] l
            LEFT JOIN [Provider] pv ON pv.Id = l.ProviderId
            LEFT JOIN [LocationType] lt ON lt.Id = l.LocationTypeId
            """,
            ["Endpoint", "ServiceName"]),

        // Storage associations. Reconciliation-owned rows are dropped (D14) via @EfpCoreServiceName.
        new ProjectionSpec(
            EntityTypes.AssignedLocation,
            """
            SELECT al.ExternalId, al.ServiceName, al.AllocationUnitCount, al.Slug, al.AggregationName,
                   CAST(al.IsDeleted AS int) AS IsDeleted, r.Name AS ResourceName,
                   l.Endpoint AS LocationEndpoint, aut.Name AS AllocationUnitType
            FROM [AssignedLocation] al
            LEFT JOIN [ProjectResource] r ON r.Id = al.ProjectResourceId
            LEFT JOIN [Location] l ON l.Id = al.LocationId
            LEFT JOIN [AllocationUnitType] aut ON aut.Id = al.AllocationUnitTypeId
            WHERE al.ServiceName IS NULL OR al.ServiceName <> @EfpCoreServiceName
            """,
            ["ExternalId", "ServiceName"]),

        new ProjectionSpec(
            EntityTypes.AssignedUserToProject,
            """
            SELECT pj.ExternalId AS ProjectExternalId, u.UserName
            FROM [AssignedUserToProject] a
            INNER JOIN [Project] pj ON pj.Id = a.ProjectId
            INNER JOIN [User] u ON u.Id = a.UserId
            """,
            ["ProjectExternalId", "UserName"]),

        new ProjectionSpec(
            EntityTypes.UserPermission,
            """
            SELECT pj.ExternalId AS ProjectExternalId, u.UserName,
                   CAST(up.SystemRoleId AS nvarchar(64)) AS Role,
                   CAST(up.IsDeleted AS int) AS IsDeleted
            FROM [UserPermission] up
            INNER JOIN [User] u ON u.Id = up.UserId
            LEFT JOIN [Project] pj ON pj.Id = up.ProjectId
            """,
            ["ProjectExternalId", "UserName", "Role"]),

        new ProjectionSpec(
            EntityTypes.UserProjectAccess,
            """
            SELECT u.UserName, pj.ExternalId AS ProjectExternalId, r.Name AS ResourceName,
                   CAST(upa.HasAccess AS int) AS HasAccess, upa.ServiceName
            FROM [UserProjectAccess] upa
            INNER JOIN [User] u ON u.Id = upa.UserId
            LEFT JOIN [Project] pj ON pj.Id = upa.ProjectId
            LEFT JOIN [ProjectResource] r ON r.Id = upa.ProjectResourceId
            """,
            ["UserName", "ProjectExternalId", "ResourceName"]),
    ];

    /// <summary>
    /// ADR-0059/A3 candidate rows: every <c>ServiceName</c>-bearing table. Each query returns
    /// <c>ServiceName</c> plus an <c>OwnerId</c> — the row's external identifier used for instance
    /// attribution when the operator supplies the owned-id set (<c>--owned-ids</c>). The tool never
    /// filters by <c>ServiceName</c> to attribute ownership: the buggy Puhuri rows carry the EFP
    /// <c>ServiceName</c> by definition, so only the external-id set is a sound ownership signal. Rows
    /// on tables without an external id (memberships) are covered by the entities that do.
    /// </summary>
    public static IReadOnlyList<(string Table, string Sql)> ServiceNameSources { get; } =
    [
        ("Project", "SELECT ServiceName, ExternalId AS OwnerId FROM [Project]"),
        ("Provider", "SELECT ServiceName, ExternalId AS OwnerId FROM [Provider]"),
        ("ProjectResource", "SELECT ServiceName, ExternalId AS OwnerId FROM [ProjectResource]"),
        ("Location", "SELECT ServiceName, CAST(NULL AS nvarchar(64)) AS OwnerId FROM [Location]"),
        ("AssignedLocation", "SELECT ServiceName, ExternalId AS OwnerId FROM [AssignedLocation]"),
        ("UserProjectAccess", "SELECT ServiceName, CAST(NULL AS nvarchar(64)) AS OwnerId FROM [UserProjectAccess]"),
    ];
}