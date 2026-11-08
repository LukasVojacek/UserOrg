using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.ResourceRequests.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "resourceRequests");

            migrationBuilder.CreateTable(
                name: "ResourceRequests",
                schema: "resourceRequests",
                columns: table => new
                {
                    Slug = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProjectShortName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProviderName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RequesterIdpId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    State = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TokenExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WorkflowRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceRequests", x => x.Slug);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceRequests_ProjectShortName_ProviderName_ResourceName",
                schema: "resourceRequests",
                table: "ResourceRequests",
                columns: new[] { "ProjectShortName", "ProviderName", "ResourceName" },
                unique: true,
                filter: "[State] IN (N'PendingPiApproval', N'AwaitingResourceCreation')");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceRequests_TokenHash",
                schema: "resourceRequests",
                table: "ResourceRequests",
                column: "TokenHash",
                filter: "[TokenHash] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ResourceRequests",
                schema: "resourceRequests");
        }
    }
}