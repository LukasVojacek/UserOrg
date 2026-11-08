using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class UpdateProjectCommandTemplateTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Enabled",
            table: "ProjectCommandTemplate");

        migrationBuilder.AddColumn<int>(
            name: "Status",
            table: "ProjectCommandTemplate",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "DefaultServiceIntegrationUrl",
            table: "Location",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true,
            defaultValueSql: "NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Status",
            table: "ProjectCommandTemplate");

        migrationBuilder.DropColumn(
            name: "DefaultServiceIntegrationUrl",
            table: "Location");

        migrationBuilder.AddColumn<bool>(
            name: "Enabled",
            table: "ProjectCommandTemplate",
            type: "bit",
            nullable: false,
            defaultValue: false);
    }
}