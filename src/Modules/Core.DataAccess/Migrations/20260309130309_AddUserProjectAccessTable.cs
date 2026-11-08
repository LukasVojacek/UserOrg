using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddUserProjectAccessTable : Migration
{
    private static readonly string[] columns = new[] { "UserId", "ProjectId", "ProjectResourceId" };
    private static readonly string[] value = new[] { "AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UserProjectAccess",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                HasAccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                Mode = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                ServiceName = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: false, defaultValueSql: "'LEXIS'")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserProjectAccess", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserProjectAccess_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserProjectAccess_User_AssignedByUserId",
                    column: x => x.AssignedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserProjectAccess_AssignedByUserId",
            table: "UserProjectAccess",
            column: "AssignedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserProjectAccess_ProjectId",
            table: "UserProjectAccess",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_UserProjectAccess_ProjectResourceId",
            table: "UserProjectAccess",
            column: "ProjectResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_UserProjectAccess_UserId",
            table: "UserProjectAccess",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "UX_UserProjectAccess_UserId_ProjectId_ProjectResourceId",
            table: "UserProjectAccess",
            columns: columns,
            unique: true,
            filter: "[ProjectResourceId] IS NOT NULL")
            .Annotation("SqlServer:Include", value);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "UserProjectAccess");
    }
}