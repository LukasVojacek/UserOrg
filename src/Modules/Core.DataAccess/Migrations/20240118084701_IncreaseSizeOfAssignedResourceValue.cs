using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class IncreaseSizeOfAssignedResourceValue : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AlterColumn<string>(
            name: "Value",
            table: "AssignedLocationValue",
            type: "nvarchar(1500)",
            maxLength: 1500,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AlterColumn<string>(
            name: "Value",
            table: "AssignedLocationValue",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(1500)",
            oldMaxLength: 1500);
    }
}