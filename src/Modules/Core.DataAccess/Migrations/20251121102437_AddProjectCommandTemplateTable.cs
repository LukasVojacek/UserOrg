using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddProjectCommandTemplateTable : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "WorkflowExecution");

        migrationBuilder.DropTable(
            name: "WorkflowExecutionStatus");

        migrationBuilder.DropTable(
            name: "Workflow");

        migrationBuilder.DropTable(
            name: "WorkflowTemplate");

        migrationBuilder.DropTable(
            name: "WorkflowType");

        migrationBuilder.CreateTable(
            name: "ProjectCommandTemplate",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                HeappeCommandTemplateId = table.Column<int>(type: "int", nullable: false),
                Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Enabled = table.Column<bool>(type: "bit", nullable: false),
                Type = table.Column<int>(type: "int", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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

    private static readonly string[] columns = new[] { "Name", "Version" };

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectCommandTemplate");

        migrationBuilder.CreateTable(
            name: "WorkflowExecutionStatus",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowExecutionsStatus", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WorkflowType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "WorkflowTemplate",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkflowTypeId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowTemplate", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowTemplate_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_WorkflowTemplate_WorkflowType_WorkflowTypeId",
                    column: x => x.WorkflowTypeId,
                    principalTable: "WorkflowType",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "Workflow",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkflowTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Workflow", x => x.Id);
                table.ForeignKey(
                    name: "FK_Workflow_Configuration_ConfigurationId",
                    column: x => x.ConfigurationId,
                    principalTable: "Configuration",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Workflow_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Workflow_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Workflow_WorkflowTemplate_WorkflowTemplateId",
                    column: x => x.WorkflowTemplateId,
                    principalTable: "WorkflowTemplate",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "WorkflowExecution",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StatusId = table.Column<int>(type: "int", nullable: false),
                WorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OrchestrationId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowExecutions", x => x.Id);
                table.ForeignKey(
                    name: "FK_WorkflowExecution_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_WorkflowExecution_WorkflowExecutionStatus_StatusId",
                    column: x => x.StatusId,
                    principalTable: "WorkflowExecutionStatus",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_WorkflowExecution_Workflow_WorkflowId",
                    column: x => x.WorkflowId,
                    principalTable: "Workflow",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_Workflow_ConfigurationId",
            table: "Workflow",
            column: "ConfigurationId");

        migrationBuilder.CreateIndex(
            name: "IX_Workflow_CreatedByUserId",
            table: "Workflow",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Workflow_ProjectId",
            table: "Workflow",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_Workflow_WorkflowTemplateId",
            table: "Workflow",
            column: "WorkflowTemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowExecution_CreatedByUserId",
            table: "WorkflowExecution",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowExecution_StatusId",
            table: "WorkflowExecution",
            column: "StatusId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowExecution_WorkflowId",
            table: "WorkflowExecution",
            column: "WorkflowId");

        migrationBuilder.CreateIndex(
            name: "UK_WorkflowExecutions",
            table: "WorkflowExecution",
            column: "OrchestrationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTemplate_CreatedByUserId",
            table: "WorkflowTemplate",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_WorkflowTemplate_WorkflowTypeId",
            table: "WorkflowTemplate",
            column: "WorkflowTypeId");

        migrationBuilder.CreateIndex(
            name: "UK_WorkflowTemplate",
            table: "WorkflowTemplate",
            columns: columns,
            unique: true);
    }
}