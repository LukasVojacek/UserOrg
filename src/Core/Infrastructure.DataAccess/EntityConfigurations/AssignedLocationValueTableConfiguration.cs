using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class AssignedLocationValueTableConfiguration : IEntityTypeConfiguration<AssignedLocationValue>
{
    private readonly string _tableName = "AssignedLocationValue";
    public void Configure(EntityTypeBuilder<AssignedLocationValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(DbConstants.NameMaxLength)
            .HasColumnType($"nvarchar({DbConstants.NameMaxLength})")
            .IsUnicode(false);

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(DbConstants.ExtendedValueMaxLength)
            .HasColumnType($"nvarchar({DbConstants.ExtendedValueMaxLength})")
            .IsUnicode(false);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.AssignedLocation)
            .WithMany(x => x.AssignedLocationsValues)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_AssignedLocation_AssignedLocationId")
            .OnDelete(DeleteBehavior.NoAction);
    }
}