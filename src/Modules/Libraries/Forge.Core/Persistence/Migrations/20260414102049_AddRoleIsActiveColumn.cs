using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Forge.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleIsActiveColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "forge",
                table: "Roles",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "forge",
                table: "Roles");
        }
    }
}