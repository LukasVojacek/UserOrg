using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeInCommandTemplateDefinitionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Type",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition");
        }
    }
}