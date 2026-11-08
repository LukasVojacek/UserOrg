using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations
{
    /// <summary>
    /// Deliberate modular-cutover cleanup on top of the 1:1 legacy baseline (ADR-0049):
    /// 1) drops the DB-side NewId() defaults on all Guid PKs (and the legacy NewId() default on the non-PK
    ///    User.IdentityProviderUserId) — keys are now code-generated UUID v7, the IdP id is supplied by the IdP;
    /// 2) removes the objectively-dead tables Configuration, ConfigurationValue, SystemModule (+ the
    ///    UserPreference.SystemModuleId FK/index/column), verified unused in legacy and modular code;
    /// 3) tightens UserPreference.UserId and UserPermission.SystemRoleId to NOT NULL and makes
    ///    IX_UserPreference_UserId UNIQUE to match the domain model (live data has 0 violations).
    /// Hand-authored: transforms the post-#29 legacy schema into the modular model. It deliberately does NOT
    /// touch the Slug/ProviderId/OnboardingRequest additions owned by migrations #25–#29.
    /// The UserPreference drops in (2) go through *IfExists helpers because the parallel legacy v6.12.x
    /// line shipped the same removals under migration ids this assembly does not contain, so on those
    /// databases they may already be done (ADR-0100).
    /// </summary>
    public partial class ModularCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- Remove dead tables + the UserPreference.SystemModuleId overlay ---
            // The three UserPreference drops are conditional: on a database that came up the legacy
            // v6.12.x line, migration 20260715111657_SyncPendingModelChanges already removed this
            // FK / index / column. That id does not exist in this (modular) assembly, so these drops
            // still run there and must tolerate the objects already being gone (ADR-0100). EF's
            // MigrationBuilder has no IfExists variant, hence the guarded Sql helpers below.
            DropForeignKeyIfExists(migrationBuilder, "UserPreference", "FK_UserPreference_SystemModule_SystemModuleId");

            migrationBuilder.DropTable(name: "ConfigurationValue");
            migrationBuilder.DropTable(name: "SystemModule");
            migrationBuilder.DropTable(name: "Configuration");

            DropIndexIfExists(migrationBuilder, "UserPreference", "IX_UserPreference_SystemModuleId");
            DropColumnIfExists(migrationBuilder, "UserPreference", "SystemModuleId");

            // --- Drop NewId() DB defaults on every Guid PK (code-generated UUID v7) ---
            DropIdDefault(migrationBuilder, "UserProjectAccess");
            DropIdDefault(migrationBuilder, "UserPreference");
            DropIdDefault(migrationBuilder, "UserPermission");
            DropIdDefault(migrationBuilder, "User");
            DropIdDefault(migrationBuilder, "Request");
            DropIdDefault(migrationBuilder, "Provider");
            DropIdDefault(migrationBuilder, "ProjectTag");
            DropIdDefault(migrationBuilder, "ProjectResource");
            DropIdDefault(migrationBuilder, "Project");
            DropIdDefault(migrationBuilder, "Location");
            DropIdDefault(migrationBuilder, "AssignedLocationValue");
            DropIdDefault(migrationBuilder, "AssignedLocation");
            DropIdDefault(migrationBuilder, "ActionLink");

            // Drop the legacy NewId() default on the (non-PK) IdP user id — it is supplied by the IdP.
            migrationBuilder.AlterColumn<string>(
                name: "IdentityProviderUserId",
                table: "User",
                type: "nvarchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldUnicode: false,
                oldMaxLength: 150,
                oldDefaultValueSql: "NewId()");

            // --- Tighten to match the domain model (0 violating rows in live data) ---
            migrationBuilder.DropIndex(
                name: "IX_UserPreference_UserId",
                table: "UserPreference");

            migrationBuilder.AlterColumn<System.Guid>(
                name: "UserId",
                table: "UserPreference",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(System.Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SystemRoleId",
                table: "UserPermission",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPreference_UserId",
                table: "UserPreference",
                column: "UserId",
                unique: true);

            // --- Additive index from the modular model (not present in legacy) ---
            migrationBuilder.CreateIndex(
                name: "IX_AssignedProjectResourceToProject_ProjectId",
                table: "AssignedProjectResourceToProject",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssignedProjectResourceToProject_ProjectId",
                table: "AssignedProjectResourceToProject");

            migrationBuilder.DropIndex(
                name: "IX_UserPreference_UserId",
                table: "UserPreference");

            migrationBuilder.AlterColumn<int>(
                name: "SystemRoleId",
                table: "UserPermission",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<System.Guid>(
                name: "UserId",
                table: "UserPreference",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(System.Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "IdentityProviderUserId",
                table: "User",
                type: "nvarchar(150)",
                unicode: false,
                maxLength: 150,
                nullable: false,
                defaultValueSql: "NewId()",
                oldClrType: typeof(string),
                oldType: "nvarchar(150)",
                oldUnicode: false,
                oldMaxLength: 150);

            // Restore NewId() DB defaults on every Guid PK.
            RestoreIdDefault(migrationBuilder, "UserProjectAccess");
            RestoreIdDefault(migrationBuilder, "UserPreference");
            RestoreIdDefault(migrationBuilder, "UserPermission");
            RestoreIdDefault(migrationBuilder, "User");
            RestoreIdDefault(migrationBuilder, "Request");
            RestoreIdDefault(migrationBuilder, "Provider");
            RestoreIdDefault(migrationBuilder, "ProjectTag");
            RestoreIdDefault(migrationBuilder, "ProjectResource");
            RestoreIdDefault(migrationBuilder, "Project");
            RestoreIdDefault(migrationBuilder, "Location");
            RestoreIdDefault(migrationBuilder, "AssignedLocationValue");
            RestoreIdDefault(migrationBuilder, "AssignedLocation");
            RestoreIdDefault(migrationBuilder, "ActionLink");

            // Recreate the dead tables + UserPreference.SystemModuleId overlay.
            migrationBuilder.AddColumn<int>(
                name: "SystemModuleId",
                table: "UserPreference",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Configuration",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                    CreatedAt = table.Column<System.DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModifiedAt = table.Column<System.DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemModule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemModule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigurationValue",
                columns: table => new
                {
                    LocationId = table.Column<System.Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationId = table.Column<System.Guid>(type: "uniqueidentifier", nullable: false),
                    ConfigurationTypeId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<System.DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModifiedAt = table.Column<System.DateTime>(type: "datetime2(3)", nullable: true, defaultValueSql: "null"),
                    Value = table.Column<string>(type: "nvarchar(40)", unicode: false, maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigurationValue", x => new { x.LocationId, x.ConfigurationId });
                    table.ForeignKey(
                        name: "FK_ConfigurationValue_ConfigurationType_ConfigurationTypeId",
                        column: x => x.ConfigurationTypeId,
                        principalTable: "ConfigurationType",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConfigurationValue_Configuration_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "Configuration",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConfigurationValue_Location_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Location",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPreference_SystemModuleId",
                table: "UserPreference",
                column: "SystemModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreference_UserId",
                table: "UserPreference",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationValue_ConfigurationId",
                table: "ConfigurationValue",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigurationValue_ConfigurationTypeId",
                table: "ConfigurationValue",
                column: "ConfigurationTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserPreference_SystemModule_SystemModuleId",
                table: "UserPreference",
                column: "SystemModuleId",
                principalTable: "SystemModule",
                principalColumn: "Id");
        }

        private static void DropForeignKeyIfExists(MigrationBuilder migrationBuilder, string table, string foreignKey) =>
            migrationBuilder.Sql(
                $"IF OBJECT_ID(N'[{foreignKey}]', N'F') IS NOT NULL ALTER TABLE [{table}] DROP CONSTRAINT [{foreignKey}];");

        private static void DropIndexIfExists(MigrationBuilder migrationBuilder, string table, string index) =>
            migrationBuilder.Sql(
                $"IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[{table}]')) DROP INDEX [{index}] ON [{table}];");

        private static void DropColumnIfExists(MigrationBuilder migrationBuilder, string table, string column) =>
            migrationBuilder.Sql(
                $"IF COL_LENGTH(N'[{table}]', N'{column}') IS NOT NULL ALTER TABLE [{table}] DROP COLUMN [{column}];");

        private static void DropIdDefault(MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.AlterColumn<System.Guid>(
                name: "Id",
                table: table,
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(System.Guid),
                oldType: "uniqueidentifier",
                oldDefaultValueSql: "NewId()");

        private static void RestoreIdDefault(MigrationBuilder migrationBuilder, string table) =>
            migrationBuilder.AlterColumn<System.Guid>(
                name: "Id",
                table: table,
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NewId()",
                oldClrType: typeof(System.Guid),
                oldType: "uniqueidentifier");
    }
}