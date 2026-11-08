using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class AllocationUnitTypeTableConfiguration : IEntityTypeConfiguration<AllocationUnitType>
{
    private readonly string _tableName = "AllocationUnitType";
    public void Configure(EntityTypeBuilder<AllocationUnitType> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Id)
            .UseIdentityColumn(seed: 1, increment: 1);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");
    }
}