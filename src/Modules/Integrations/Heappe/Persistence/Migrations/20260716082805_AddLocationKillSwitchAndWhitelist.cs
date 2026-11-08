using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLocationKillSwitchAndWhitelist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationCommandTemplateKillSwitch",
                schema: "heappe_integration",
                columns: table => new
                {
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsEnforced = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", unicode: false, maxLength: 500, nullable: false),
                    EnforcedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    EnforcedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LiftedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    LiftedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationCommandTemplateKillSwitch", x => x.LocationId);
                });

            migrationBuilder.CreateTable(
                name: "LocationCommandTemplateWhitelist",
                schema: "heappe_integration",
                columns: table => new
                {
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectCommandTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationCommandTemplateWhitelist", x => new { x.LocationId, x.ProjectCommandTemplateId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationCommandTemplateKillSwitch",
                schema: "heappe_integration");

            migrationBuilder.DropTable(
                name: "LocationCommandTemplateWhitelist",
                schema: "heappe_integration");
        }
    }
}