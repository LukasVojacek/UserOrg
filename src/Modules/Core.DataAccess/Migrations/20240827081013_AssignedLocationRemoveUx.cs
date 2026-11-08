using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class AssignedLocationRemoveUx : Migration
{

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation");

        migrationBuilder.CreateIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation",
            columns: new[] { "LocationId", "ProjectResourceId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation");

        migrationBuilder.CreateIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation",
            columns: new[] { "LocationId", "ProjectResourceId" },
            unique: true);
    }
}