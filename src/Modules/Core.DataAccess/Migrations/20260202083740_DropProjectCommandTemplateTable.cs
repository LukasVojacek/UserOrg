using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class DropProjectCommandTemplateTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectCommandTemplate");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectCommandTemplate",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                HeappeCommandTemplateId = table.Column<int>(type: "int", nullable: false),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Type = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectCommandTemplate", x => x.Id)
                    .Annotation("SqlServer:Clustered", true);
                table.ForeignKey(
                    name: "FK_ProjectCommandTemplate_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_ProjectCommandTemplate_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectCommandTemplate_ProjectId",
            table: "ProjectCommandTemplate",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectCommandTemplate_ProjectResourceId",
            table: "ProjectCommandTemplate",
            column: "ProjectResourceId");
    }
}