using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class UpdateProviderEmail : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AlterColumn<string>(
            name: "SupportEmail",
            table: "Provider",
            type: "nvarchar(60)",
            maxLength: 60,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(60)",
            oldMaxLength: 60);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AlterColumn<string>(
            name: "SupportEmail",
            table: "Provider",
            type: "nvarchar(60)",
            maxLength: 60,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(60)",
            oldMaxLength: 60,
            oldNullable: true);
    }
}