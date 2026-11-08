using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddRequestAndAssignedLocationSlug : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Request",
                type: "nvarchar(120)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "AssignedLocation",
                type: "nvarchar(120)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Request_Slug",
                table: "Request",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssignedLocation_Slug",
                table: "AssignedLocation",
                column: "Slug",
                unique: true,
                filter: "[Slug] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Request_Slug",
                table: "Request");

            migrationBuilder.DropIndex(
                name: "IX_AssignedLocation_Slug",
                table: "AssignedLocation");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Request");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "AssignedLocation");
        }
    }
}