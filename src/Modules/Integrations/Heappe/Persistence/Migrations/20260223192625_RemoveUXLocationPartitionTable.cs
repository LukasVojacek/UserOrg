using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUXLocationPartitionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LocationPartition_LocationId_Name",
                schema: "heappe_integration",
                table: "LocationPartition");

            migrationBuilder.CreateIndex(
                name: "UX_LocationPartition_LocationId_Name",
                schema: "heappe_integration",
                table: "LocationPartition",
                columns: new[] { "LocationId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_LocationPartition_LocationId_Name",
                schema: "heappe_integration",
                table: "LocationPartition");

            migrationBuilder.CreateIndex(
                name: "UX_LocationPartition_LocationId_Name",
                schema: "heappe_integration",
                table: "LocationPartition",
                columns: new[] { "LocationId", "Name" },
                unique: true);
        }
    }
}