using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKillSwitchLockdownAndWhitelistAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AddedAtUtc",
                schema: "heappe_integration",
                table: "LocationCommandTemplateWhitelist",
                type: "datetime2(3)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "AddedByUserId",
                schema: "heappe_integration",
                table: "LocationCommandTemplateWhitelist",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreWhitelist",
                schema: "heappe_integration",
                table: "LocationCommandTemplateKillSwitch",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddedAtUtc",
                schema: "heappe_integration",
                table: "LocationCommandTemplateWhitelist");

            migrationBuilder.DropColumn(
                name: "AddedByUserId",
                schema: "heappe_integration",
                table: "LocationCommandTemplateWhitelist");

            migrationBuilder.DropColumn(
                name: "IgnoreWhitelist",
                schema: "heappe_integration",
                table: "LocationCommandTemplateKillSwitch");
        }
    }
}