using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class RemoveHistoryTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ConfigurationHistory");

        migrationBuilder.DropTable(
            name: "ProjectHistory");

        migrationBuilder.DropTable(
            name: "ProjectResourceHistory");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConfigurationHistory",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfigurationHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_ConfigurationHistory_Configuration_ConfigurationId",
                    column: x => x.ConfigurationId,
                    principalTable: "Configuration",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ConfigurationHistory_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "ProjectHistory",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectHistory_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ProjectHistory_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "ProjectResourceHistory",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectResourceHistory", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectResourceHistory_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ProjectResourceHistory_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationHistory_ConfigurationId",
            table: "ConfigurationHistory",
            column: "ConfigurationId");

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationHistory_CreatedByUserId",
            table: "ConfigurationHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectHistory_CreatedByUserId",
            table: "ProjectHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectHistory_ProjectId",
            table: "ProjectHistory",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResourceHistory_CreatedByUserId",
            table: "ProjectResourceHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResourceHistory_ProjectResourceId",
            table: "ProjectResourceHistory",
            column: "ProjectResourceId");
    }
}