using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class RemoveRolePermissionsTables : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_UserPermission_SystemRole_SystemRoleId",
            table: "UserPermission");

        migrationBuilder.DropTable(
            name: "AssignedSystemPermissionTypeToSystemRole");

        migrationBuilder.DropTable(
            name: "SystemPermissionType");

        migrationBuilder.DropTable(
            name: "SystemRole");

        migrationBuilder.DropIndex(
            name: "IX_UserPermission_SystemRoleId",
            table: "UserPermission");

        migrationBuilder.AlterColumn<int>(
            name: "SystemRoleId",
            table: "UserPermission",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(
            name: "SystemRoleId",
            table: "UserPermission",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.CreateTable(
            name: "SystemPermissionType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemPermissionType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SystemRole",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BaseSystemRoleId = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                IsReserved = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemRole", x => x.Id);
                table.ForeignKey(
                    name: "FK_SystemRole_SystemRole_BaseSystemRoleId",
                    column: x => x.BaseSystemRoleId,
                    principalTable: "SystemRole",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AssignedSystemPermissionTypeToSystemRole",
            columns: table => new
            {
                SystemPermissionTypeId = table.Column<int>(type: "int", nullable: false),
                SystemRoleId = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedSystemPermissionTypeToSystemRole", x => new { x.SystemPermissionTypeId, x.SystemRoleId });
                table.ForeignKey(
                    name: "FK_AssignedSystemPermissionTypeToSystemRole_SystemPermissionType_SystemPermissionTypeId",
                    column: x => x.SystemPermissionTypeId,
                    principalTable: "SystemPermissionType",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedSystemPermissionTypeToSystemRole_SystemRole_SystemRoleId",
                    column: x => x.SystemRoleId,
                    principalTable: "SystemRole",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateIndex(
            name: "IX_UserPermission_SystemRoleId",
            table: "UserPermission",
            column: "SystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedSystemPermissionTypeToSystemRole_SystemRoleId",
            table: "AssignedSystemPermissionTypeToSystemRole",
            column: "SystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_SystemRole_BaseSystemRoleId",
            table: "SystemRole",
            column: "BaseSystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_SystemRole_Name",
            table: "SystemRole",
            column: "Name",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_UserPermission_SystemRole_SystemRoleId",
            table: "UserPermission",
            column: "SystemRoleId",
            principalTable: "SystemRole",
            principalColumn: "Id");
    }
}