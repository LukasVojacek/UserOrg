using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrg.Shared.Persistance;

using UserOrganizationManagement.Domain.Entities;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class UserPreferenceTableConfiguration : IEntityTypeConfiguration<UserPreference>
{
    private readonly string _tableName = "UserPreference";
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Data)
            .IsRequired()
            .HasMaxLength(DbConstants.DataMaxLength)
            .HasColumnType($"nvarchar({DbConstants.DataMaxLength})")
            .IsUnicode(false);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.User)
            .WithMany()
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_User_UserId")
            .OnDelete(DeleteBehavior.NoAction);

        // The SystemModule navigation is not read or written by any query, yet mapping it makes EF add
        // SystemModuleId to every SELECT on UserPreference. The column is dropped by ModularCleanup, so the
        // read fails with SqlException 207 (Invalid column name) -> 500 on GET /api/User/Preferences.
        // Ignore the unused navigation so no shadow FK is inferred by convention.
        builder.Ignore(x => x.SystemModule);
    }
}