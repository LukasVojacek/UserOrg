using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandTemplateLocationDefinitionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionCommand",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition");

            migrationBuilder.DropColumn(
                name: "PreparationCommand",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition");

            migrationBuilder.CreateTable(
                name: "CommandTemplateLocationDefinition",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    PreparationCommand = table.Column<string>(type: "nvarchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ExecutionCommand = table.Column<string>(type: "nvarchar(500)", unicode: false, maxLength: 500, nullable: true),
                    LocationType = table.Column<int>(type: "int", nullable: false),
                    CommandTemplateDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationPartitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandTemplateLocationDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandTemplateLocationDefinition_CommandTemplateDefinition_CommandTemplateDefinitionId",
                        column: x => x.CommandTemplateDefinitionId,
                        principalSchema: "heappe_integration",
                        principalTable: "CommandTemplateDefinition",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommandTemplateLocationDefinition_CommandTemplateDefinitionId",
                schema: "heappe_integration",
                table: "CommandTemplateLocationDefinition",
                column: "CommandTemplateDefinitionId");

            migrationBuilder.CreateIndex(
                name: "UX_CommandTemplateLocationDefinition_LocationType_CommandTemplateDefinitionId_LocationPartitionId",
                schema: "heappe_integration",
                table: "CommandTemplateLocationDefinition",
                columns: new[] { "LocationType", "CommandTemplateDefinitionId", "LocationPartitionId" },
                unique: true,
                filter: "[LocationPartitionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CommandTemplateLocationDefinition_LocationType_CommandTemplateDefinitionId_NullLocationPartitionId",
                schema: "heappe_integration",
                table: "CommandTemplateLocationDefinition",
                columns: new[] { "LocationType", "CommandTemplateDefinitionId" },
                unique: true,
                filter: "[LocationPartitionId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandTemplateLocationDefinition",
                schema: "heappe_integration");

            migrationBuilder.AddColumn<string>(
                name: "ExecutionCommand",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition",
                type: "nvarchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparationCommand",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition",
                type: "nvarchar(500)",
                unicode: false,
                maxLength: 500,
                nullable: true);
        }
    }
}