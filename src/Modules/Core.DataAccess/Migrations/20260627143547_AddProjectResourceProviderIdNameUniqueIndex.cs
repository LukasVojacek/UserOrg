using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectResourceProviderIdNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectResource_ProviderId",
                table: "ProjectResource");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_ProviderId_Name",
                table: "ProjectResource",
                columns: new[] { "ProviderId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProviderId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectResource_ProviderId_Name",
                table: "ProjectResource");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_ProviderId",
                table: "ProjectResource",
                column: "ProviderId");
        }
    }
}