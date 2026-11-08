using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModifyProjectCommandTemplateTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                type: "datetime2(3)",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                type: "datetime2(3)",
                nullable: true,
                defaultValueSql: "null");
        }
    }
}