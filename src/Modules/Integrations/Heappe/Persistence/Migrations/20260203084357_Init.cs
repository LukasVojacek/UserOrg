using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Heappe.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "heappe_integration");

            migrationBuilder.CreateTable(
                name: "CommandTemplateDefinition",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    Name = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", unicode: false, maxLength: 150, nullable: true),
                    PreparationCommand = table.Column<string>(type: "nvarchar(500)", unicode: false, maxLength: 500, nullable: true),
                    ExecutionCommand = table.Column<string>(type: "nvarchar(500)", unicode: false, maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandTemplateDefinition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationPartition",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    Name = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", unicode: false, maxLength: 150, nullable: true),
                    AggregationName = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: true),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationPartition", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CommandTemplateParameterDefinition",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    Name = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(150)", unicode: false, maxLength: 150, nullable: true),
                    CommandTemplateDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandTemplateParameterDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommandTemplateParameterDefinition_CommandTemplateDefinition_CommandTemplateDefinitionId",
                        column: x => x.CommandTemplateDefinitionId,
                        principalSchema: "heappe_integration",
                        principalTable: "CommandTemplateDefinition",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommandTemplate",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    Name = table.Column<string>(type: "nvarchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    CommandTemplateDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommandTemplate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommandTemplate_CommandTemplateDefinition",
                        column: x => x.CommandTemplateDefinitionId,
                        principalSchema: "heappe_integration",
                        principalTable: "CommandTemplateDefinition",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProjectCommandTemplatePartition",
                schema: "heappe_integration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    CommandTemplateId = table.Column<long>(type: "bigint", nullable: true, defaultValueSql: "null"),
                    State = table.Column<int>(type: "int", nullable: false),
                    LocationPartitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectCommandTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ModifiedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCommandTemplatePartition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectCommandTemplatePartition_LocationPartition",
                        column: x => x.LocationPartitionId,
                        principalSchema: "heappe_integration",
                        principalTable: "LocationPartition",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProjectCommandTemplatePartition_ProjectCommandTemplate",
                        column: x => x.ProjectCommandTemplateId,
                        principalSchema: "heappe_integration",
                        principalTable: "ProjectCommandTemplate",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_CommandTemplateDefinition_Name",
                schema: "heappe_integration",
                table: "CommandTemplateDefinition",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandTemplateParameterDefinition_CommandTemplateDefinitionId",
                schema: "heappe_integration",
                table: "CommandTemplateParameterDefinition",
                column: "CommandTemplateDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationPartition_LocationId",
                schema: "heappe_integration",
                table: "LocationPartition",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "UX_LocationPartition_LocationId_Name",
                schema: "heappe_integration",
                table: "LocationPartition",
                columns: new[] { "LocationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_LocationPartition_LocationId_Name_AggregationName",
                schema: "heappe_integration",
                table: "LocationPartition",
                columns: new[] { "LocationId", "Name", "AggregationName" },
                unique: true,
                filter: "[AggregationName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplate_CommandTemplateDefinitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                column: "CommandTemplateDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplate_ProjectResourceId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                column: "ProjectResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplate_Type",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "UX_ProjectCommandTemplate_ProjectId_Name",
                schema: "heappe_integration",
                table: "ProjectCommandTemplate",
                columns: new[] { "ProjectId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplatePartition_CommandTemplateId_State",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                columns: new[] { "CommandTemplateId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplatePartition_LocationPartitionId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "LocationPartitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCommandTemplatePartition_ProjectCommandTemplateId",
                schema: "heappe_integration",
                table: "ProjectCommandTemplatePartition",
                column: "ProjectCommandTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandTemplateParameterDefinition",
                schema: "heappe_integration");

            migrationBuilder.DropTable(
                name: "ProjectCommandTemplatePartition",
                schema: "heappe_integration");

            migrationBuilder.DropTable(
                name: "LocationPartition",
                schema: "heappe_integration");

            migrationBuilder.DropTable(
                name: "ProjectCommandTemplate",
                schema: "heappe_integration");

            migrationBuilder.DropTable(
                name: "CommandTemplateDefinition",
                schema: "heappe_integration");
        }
    }
}