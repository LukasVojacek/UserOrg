using Core.Domain.Shared;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Core.DataAccess.Configurations;

/// <summary>
/// Maps Project domain entity to Project table in database.
/// Handles shadow properties and relationships to match existing schema.
/// </summary>
public class ProjectConfiguration : IEntityTypeConfiguration<Domain.Project.Project>
{
    public void Configure(EntityTypeBuilder<Domain.Project.Project> builder)
    {
        // Shadow PK typed as ProjectId VO so FK relationships match CLR types
        // ValueGeneratedNever: ID is generated in domain model (ProjectId.New() → Guid V7)
        builder.Property<ProjectId>("Id")
            .HasConversion(id => id.Value, value => ProjectId.From(value))
            .ValueGeneratedNever();

        // Preserve the legacy PK constraint name so no migration is generated.
        builder.HasKey("Id").HasName("PK_ProjectConfiguration");

        // Natural identifier: ShortName — backed by the existing unique index UX_Project_ProjectShortName.
        // NOT modeled as an alternate key (no AK_Project_ShortName exists in the deployed schema).
        builder.Property(p => p.ShortName)
            .HasConversion(
                sn => sn.Value,  // ProjectShortName → string for storage
                value => ProjectShortName.From(value))  // string → ProjectShortName for domain
            .IsRequired()
            .HasMaxLength(50)
            .IsUnicode(true);

        builder.HasIndex(p => p.ShortName)
            .IsUnique()
            .HasDatabaseName("UX_Project_ProjectShortName");

        builder.Property(p => p.Name)
            .HasConversion(
                name => name.Value,  // ProjectName → string for storage
                value => ProjectName.From(value))  // string → ProjectName for domain
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(true);

        // Description - nullable [Description] column ↔ nullable Description? property (null when unset). ADR-0010.
        builder.Property(p => p.Description)
            .HasConversion<DescriptionNullableConverter>()
            .HasMaxLength(500)
            .IsUnicode(true);

        // Email - nullable [Email] column ↔ nullable Email? property (null when unset). ADR-0010.
        builder.Property(p => p.Email)
            .HasConversion<EmailNullableConverter>()
            .HasMaxLength(100)
            .IsUnicode(false)
            .HasColumnType("nvarchar(100)");

        builder.Property(p => p.ExternalId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : null,
                value => string.IsNullOrEmpty(value) ? (ExternalId?)null : ExternalId.Create(value))
            .HasMaxLength(200)
            .IsUnicode(false)
            .HasColumnType("nvarchar(200)")
            .HasDefaultValueSql("null");

        builder.Property(p => p.Mode)
            .HasConversion(
                mode => (int)mode,  // EOperationMode → int for storage
                value => (EOperationMode)value)  // int → EOperationMode for domain
            .HasDefaultValue(EOperationMode.Unspecified);

        // ServiceName - maps to [ServiceName] column (DB default constraint 'LEXIS', matches legacy).
        builder.Property(p => p.ServiceName)
            .HasConversion(sn => sn.Value, value => ServiceName.Create(value))
            .IsRequired()
            .HasMaxLength(100)
            .IsUnicode(false)
            .HasColumnType("nvarchar(100)")
            .HasDefaultValueSql("'LEXIS'");

        // StartDate - maps to [StartDate] column (datetime2(3), matches legacy)
        builder.Property(p => p.StartDate)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .IsRequired();

        // EndDate - maps to [EndDate] column (datetime2(3), matches legacy)
        builder.Property(p => p.EndDate)
            .HasConversion<UtcDateTimeOffsetConverter>()
            .HasColumnType("datetime2(3)")
            .IsRequired();

        builder.Property<ProjectDomainTypeId>("ProjectDomainTypeId")
            .HasConversion(id => id.Value, value => new ProjectDomainTypeId(value))
            .IsRequired();
        builder.HasIndex("ProjectDomainTypeId")
            .HasDatabaseName("IX_Project_ProjectDomainTypeId");

        builder.HasOne<ProjectDomainType>("_projectDomainType")
            .WithMany()
            .HasForeignKey("ProjectDomainTypeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.NoAction);

        // DomainType CLR property (DomainTypeName VO) cannot be mapped directly;
        // the DB uses ProjectDomainTypeId (int) for referential integrity.
        builder.Ignore(x => x.DomainType);

        // CreatedByUserId - cross-aggregate FK mapped via ValueConverter
        // Stores UserId (Guid) in DB as [CreatedByUserId] column
        // DDD ideal: store UserName (natural key of User aggregate) to avoid coupling to surrogate PK.
        // Requires DB migration: ALTER TABLE Project ALTER COLUMN CreatedByUserId NVARCHAR(255),
        // backfill: UPDATE p SET CreatedByUserId = u.UserName FROM Project p JOIN [User] u ON u.Id = p.CreatedByUserId,
        // then retarget FK to User.UserName (alternate key).
        builder.Property(p => p.CreatedBy)
            .HasConversion(
                id => id.Value,  // UserId → Guid for storage
                value => UserId.From(value))  // Guid → UserId for domain
            .IsRequired()
            .HasColumnName("CreatedByUserId");

        builder.HasIndex(p => p.CreatedBy)
            .HasDatabaseName("IX_Project_CreatedByUserId");

        builder.HasOne<Domain.User.User>()
            .WithMany()
            .IsRequired()
            .HasForeignKey(p => p.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("FK_Project_User_CreatedByUserId");

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false);

        // Shadow properties for audit fields (managed by EF, not exposed in domain model)
        builder.Property<DateTimeOffset>("CreatedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired()
            .HasDefaultValueSql("SYSUTCDATETIME()");

        builder.Property<DateTimeOffset?>("ModifiedAt")
            .HasConversion<UtcDateTimeOffsetConverter>()
            .IsRequired(false)
            .HasDefaultValueSql("null");

        builder.HasMany<ProjectTag>("_projectTags")
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "AssignedProjectTagToProject",
                j => j.HasOne<ProjectTag>()
                      .WithMany()
                      .HasForeignKey("ProjectTagsId"),
                j => j.HasOne<Domain.Project.Project>()
                      .WithMany()
                      .HasForeignKey("ProjectsId"),
                j =>
                {
                    j.HasKey("ProjectTagsId", "ProjectsId");
                    j.ToTable("AssignedProjectTagToProject");
                    j.Property<Guid>("ProjectTagsId").HasColumnName("ProjectTagsId");
                    j.Property<ProjectId>("ProjectsId")
                        .HasConversion(id => id.Value, value => ProjectId.From(value))
                        .HasColumnName("ProjectsId");
                });

        // why: ClientCascade per ADR-0115. The database FK stays NO ACTION.
        builder.HasMany<AssignedUserToProject>("_assignedUsers")
            .WithOne("Project")
            .HasForeignKey("ProjectId")
            .IsRequired()
            .OnDelete(DeleteBehavior.ClientCascade)
            .HasConstraintName("FK_AssignedUserToProject_Project_ProjectId");

        // why: ClientCascade per ADR-0115. The database FK stays NO ACTION.
        builder.HasMany<AssignedProjectResourceToProject>("_assignedResources")
            .WithOne("Project")
            .HasForeignKey("ProjectId")
            .IsRequired()
            .OnDelete(DeleteBehavior.ClientCascade);

        // Project role rows (UserPermission with ProjectId set) owned by the Project aggregate alongside
        // membership (ADR-0008). Maps the existing FK_UserPermission_Project_ProjectId; the shared User
        // navigation (inverse on the UserId relationship) is configured in UserConfiguration.
        // No inverse navigation on the Project side. ProjectId is nullable (system roles have null), so the
        // relationship is optional. Hydrated via IgnoreQueryFilters(["SystemRoleOnly"]) in ProjectRepository.
        builder.HasMany<UserPermission>("_memberRoles")
            .WithOne()
            .HasForeignKey(up => up.ProjectId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasQueryFilter(p => !p.IsDeleted);

        builder.ToTable("Project");
    }
}