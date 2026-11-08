using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;

namespace UserOrganizationManagement.Infrastructure.DataAccess.EntityConfigurations;

public class ActionLinkTableConfiguration : IEntityTypeConfiguration<ActionLink>
{
    private readonly string _tableName = "ActionLink";
    public void Configure(EntityTypeBuilder<ActionLink> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(_tableName);

        builder.Property(x => x.Action)
            .IsRequired()
            .HasDefaultValue(EActionType.Unknown)
            .HasConversion(
                v => (int)v,
                v => (EActionType)v);

        builder.HasKey(x => x.Id)
            .HasName($"PK_{_tableName}");

        builder.HasOne(x => x.Request)
            .WithMany(x => x.ActionLinks)
            .IsRequired()
            .HasConstraintName($"FK_{_tableName}_Request_RequestId")
            .HasForeignKey(x => x.RequestId);
    }
}