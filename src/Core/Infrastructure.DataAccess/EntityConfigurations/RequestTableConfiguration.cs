using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class RequestTableConfiguration : IEntityTypeConfiguration<Request>
{
    private readonly string _tableName = "Request";
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.State)
            .IsRequired()
            .HasDefaultValue(ERequestState.Pending)
            .HasConversion(
                v => (int)v,
                v => (ERequestState)v);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasDefaultValue(ERequestType.Unknown)
            .HasConversion(
                v => (int)v,
                v => (ERequestType)v);

        // ADR-0024: GUID-free public addressing slug (nullable; v1 rows stay null).
        builder.Property(x => x.Slug)
            .HasColumnType("nvarchar(120)");

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("[Slug] IS NOT NULL")
            .HasDatabaseName($"IX_{_tableName}_Slug");

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.User)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_User_UserId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Project)
            .WithMany()
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Project_ProjectId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.ProjectResource)
            .WithMany()
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_ProjectResource_ProjectResourceId")
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.Provider)
            .WithMany()
            .IsRequired(false)
            .HasConstraintName($"FK_{_tableName}_Provider_ProviderId")
            .OnDelete(DeleteBehavior.NoAction);

    }
}