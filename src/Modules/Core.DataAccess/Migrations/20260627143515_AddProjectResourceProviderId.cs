using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectResourceProviderId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderId",
                table: "ProjectResource",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectResource_ProviderId",
                table: "ProjectResource",
                column: "ProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProjectResource_Provider_ProviderId",
                table: "ProjectResource",
                column: "ProviderId",
                principalTable: "Provider",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProjectResource_Provider_ProviderId",
                table: "ProjectResource");

            migrationBuilder.DropIndex(
                name: "IX_ProjectResource_ProviderId",
                table: "ProjectResource");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "ProjectResource");
        }
    }
}