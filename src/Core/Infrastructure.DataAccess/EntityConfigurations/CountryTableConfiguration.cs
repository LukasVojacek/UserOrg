using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class CountryTableConfiguration : IEntityTypeConfiguration<Country>
{
    private readonly string _tableName = "Country";
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("Country");

        builder.Property(x => x.Id)
            .UseIdentityColumn(seed: 1, increment: 1);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");
    }
}