using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendCmdTemplateNameInProjectCommandTemplateTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                type: "nvarchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldUnicode: false,
                oldMaxLength: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                type: "nvarchar(100)",
                unicode: false,
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldUnicode: false,
                oldMaxLength: 200);
        }
    }
}