using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAuditableEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplate_CommandTemplateDefinition",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplatePartition_LocationPartition",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplatePartition_ProjectCommandTemplate",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplate_CommandTemplateDefinition_CommandTemplateDefinitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                column: "CommandTemplateDefinitionId",
                principalSchema: "heappe_integration",
                principalTable: "CommandTemplateDefinition",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplatePartition_LocationPartition_LocationPartitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "LocationPartitionId",
                principalSchema: "heappe_integration",
                principalTable: "LocationPartition",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplatePartition_ProjectCommandTemplate_ProjectCommandTemplateId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "ProjectCommandTemplateId",
                principalSchema: "heappe_integration",
                principalTable: "ProjectCommandTemplate",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplate_CommandTemplateDefinition_CommandTemplateDefinitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplatePartition_LocationPartition_LocationPartitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition");

            migrationBuilder.DropForeignKey(
                name: "FK_ProjectCommandTemplatePartition_ProjectCommandTemplate_ProjectCommandTemplateId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplate_CommandTemplateDefinition",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                column: "CommandTemplateDefinitionId",
                principalSchema: "heappe_integration",
                principalTable: "CommandTemplateDefinition",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplatePartition_LocationPartition",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "LocationPartitionId",
                principalSchema: "heappe_integration",
                principalTable: "LocationPartition",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectCommandTemplatePartition_ProjectCommandTemplate",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "ProjectCommandTemplateId",
                principalSchema: "heappe_integration",
                principalTable: "ProjectCommandTemplate",
                principalColumn: "Id");
        }
    }
}