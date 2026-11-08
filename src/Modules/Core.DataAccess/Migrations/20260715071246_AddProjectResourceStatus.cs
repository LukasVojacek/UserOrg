using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectResourceStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "ProjectResource",
                type: "int",
                nullable: false,
                defaultValue: (int)EProjectResourceState.Published);

            migrationBuilder.AlterColumn<int>(
                name: "State",
                table: "ProjectResource",
                type: "int",
                nullable: false,
                defaultValue: (int)EProjectResourceState.Draft);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "ProjectResource");
        }
    }
}