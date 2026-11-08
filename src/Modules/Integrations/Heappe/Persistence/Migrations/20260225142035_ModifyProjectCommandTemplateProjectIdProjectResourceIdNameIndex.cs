using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModifyProjectCommandTemplateProjectIdProjectResourceIdNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectCommandTemplate_ProjectId_Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectCommandTemplate_ProjectId_ProjectResourceId_Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                columns: new[] { "ProjectId", "ProjectResourceId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ProjectCommandTemplate_ProjectId_ProjectResourceId_Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectCommandTemplate_ProjectId_Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }
    }
}