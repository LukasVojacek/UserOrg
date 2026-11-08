using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class DeleteSidInUserEntity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
            name: "Sid",
            table: "User");

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<Guid>(
            name: "Sid",
            table: "User",
            type: "uniqueidentifier",
            nullable: false,
            defaultValue: Guid.Empty);
}