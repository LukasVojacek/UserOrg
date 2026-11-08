using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class ExtendRequestEntity : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<string>(
            name: "ContactEmail",
            table: "Request",
            type: "nvarchar(max)",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ProviderId",
            table: "Request",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Request_ProviderId",
            table: "Request",
            column: "ProviderId");

        migrationBuilder.AddForeignKey(
            name: "FK_Request_Provider_ProviderId",
            table: "Request",
            column: "ProviderId",
            principalTable: "Provider",
            principalColumn: "Id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropForeignKey(
            name: "FK_Request_Provider_ProviderId",
            table: "Request");

        migrationBuilder.DropIndex(
            name: "IX_Request_ProviderId",
            table: "Request");

        migrationBuilder.DropColumn(
            name: "ContactEmail",
            table: "Request");

        migrationBuilder.DropColumn(
            name: "ProviderId",
            table: "Request");
    }
}