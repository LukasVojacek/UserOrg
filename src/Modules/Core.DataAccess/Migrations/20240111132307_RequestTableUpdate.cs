using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class RequestTableUpdate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropForeignKey(
            name: "FK_ProjectResource_ProjectResourceStatus_ProjectResourceStatusId",
            table: "ProjectResource");

        migrationBuilder.DropTable(
            name: "ProjectResourceStatus");

        migrationBuilder.DropTable(
            name: "SelfOnboardingRequest");

        migrationBuilder.DropIndex(
            name: "IX_ProjectResource_ProjectResourceStatusId",
            table: "ProjectResource");

        migrationBuilder.DropColumn(
            name: "ProjectResourceStatusId",
            table: "ProjectResource");

        migrationBuilder.AlterColumn<string>(
            name: "Description",
            table: "Project",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(150)",
            oldMaxLength: 150,
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "Request",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                State = table.Column<int>(type: "int", nullable: false),
                Type = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Request", x => x.Id);
                table.ForeignKey(
                    name: "FK_Request_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Request_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Request_User_UserId",
                    column: x => x.UserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Request_ProjectId",
            table: "Request",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_Request_ProjectResourceId",
            table: "Request",
            column: "ProjectResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_Request_UserId",
            table: "Request",
            column: "UserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(
            name: "Request");

        migrationBuilder.AddColumn<int>(
            name: "ProjectResourceStatusId",
            table: "ProjectResource",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AlterColumn<string>(
            name: "Description",
            table: "Project",
            type: "nvarchar(150)",
            maxLength: 150,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(1000)",
            oldMaxLength: 1000,
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "ProjectResourceStatus",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectResourceStatus", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SelfOnboardingRequest",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                State = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SelfOnboardingRequest", x => x.Id);
                table.ForeignKey(
                    name: "FK_SelfOnboardingRequest_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_SelfOnboardingRequest_User_UserId",
                    column: x => x.UserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResource_ProjectResourceStatusId",
            table: "ProjectResource",
            column: "ProjectResourceStatusId");

        migrationBuilder.CreateIndex(
            name: "IX_SelfOnboardingRequest_ProjectId",
            table: "SelfOnboardingRequest",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_SelfOnboardingRequest_UserId",
            table: "SelfOnboardingRequest",
            column: "UserId");

        migrationBuilder.AddForeignKey(
            name: "FK_ProjectResource_ProjectResourceStatus_ProjectResourceStatusId",
            table: "ProjectResource",
            column: "ProjectResourceStatusId",
            principalTable: "ProjectResourceStatus",
            principalColumn: "Id");
    }
}