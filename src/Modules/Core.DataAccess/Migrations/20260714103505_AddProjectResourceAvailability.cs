using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectResourceAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR-0065: draft lifecycle. Availability 0 = Active, 1 = Draft. The non-null default of 0
            // backfills every existing ProjectResource row to Active — pre-existing resources are provisioned.
            migrationBuilder.AddColumn<int>(
                name: "Availability",
                table: "ProjectResource",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Availability",
                table: "ProjectResource");
        }
    }
}