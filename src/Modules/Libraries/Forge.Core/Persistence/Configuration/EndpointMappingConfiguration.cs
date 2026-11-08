using Forge.Core.Features.EndpointMap.Persistence;
using Forge.Core.Features.Permission.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

namespace Forge.Core.Persistence.Configuration;

internal sealed class EndpointMappingConfiguration : IEntityTypeConfiguration<EndpointMapping>
{
    public void Configure(EntityTypeBuilder<EndpointMapping> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EndpointMappings");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();

        builder.Property(e => e.EndpointName)
            .IsRequired()
            .HasMaxLength(DbConstants.EndpointNameMaxLength)
            .IsUnicode(false);

        builder.HasIndex(e => e.EndpointName)
            .IsUnique()
            .HasDatabaseName("IX_EndpointMappings_EndpointName_Unique");

        builder.Property(e => e.Description)
            .HasMaxLength(DbConstants.ExtendedDescriptionMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedDescriptionMaxLength})");

        // Path template and HttpMethod for path-based lookup
        builder.Property(e => e.PathTemplate)
            .HasMaxLength(1000)
            .IsUnicode(false);

        builder.Property(e => e.HttpMethod)
            .HasMaxLength(10)
            .IsUnicode(false);

        // Shadow property for CreatedAt: not present on CLR model, generated on add by DB
        builder.Property<DateTimeOffset>("CreatedAt")
            .IsRequired()
            .HasColumnType("datetimeoffset(3)")
            .HasDefaultValueSql("SYSDATETIMEOFFSET()")
            .ValueGeneratedOnAdd();

        // Many-to-many: EndpointMappings <-> Permissions via join table EndpointMapping_Permission
        builder
            .HasMany(e => e.Permissions)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "EndpointMapping_Permission",
                right => right
                    .HasOne<Permission>()
                    .WithMany()
                    .HasForeignKey("PermissionId")
                    .OnDelete(DeleteBehavior.Restrict),
                left => left
                    .HasOne<EndpointMapping>()
                    .WithMany()
                    .HasForeignKey("EndpointMappingId")
                    .OnDelete(DeleteBehavior.Restrict),
                je =>
                {
                    je.HasKey("EndpointMappingId", "PermissionId");
                    je.ToTable("EndpointMapping_Permission");
                    je.HasIndex(["PermissionId", "EndpointMappingId"]).HasDatabaseName("IX_EndpointMapping_Permission_PermissionId_EndpointMappingId");
                });
    }
}