using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddExternalIdAndOperationModeColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ProjectResource_Name",
            table: "ProjectResource");

        migrationBuilder.RenameIndex(
            name: "IX_User_IdentityProviderUserId",
            table: "User",
            newName: "UX_User_IdentityProviderUserId");

        migrationBuilder.RenameIndex(
            name: "UK_ProjectShortName",
            table: "Project",
            newName: "UX_Project_ProjectShortName");

        migrationBuilder.RenameIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation",
            newName: "IX_AssignedLocation_LocationId_ProjectResourceId");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "UserPreference",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Data",
            table: "UserPreference",
            type: "nvarchar(4000)",
            unicode: false,
            maxLength: 4000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(max)",
            oldMaxLength: 5000);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "UserPreference",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "UserPermission",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "UserPermission",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "User",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "User",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<int>(
            name: "Type",
            table: "Request",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<int>(
            name: "State",
            table: "Request",
            type: "int",
            nullable: false,
            defaultValue: 1,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Request",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Request",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<string>(
            name: "SupportEmail",
            table: "Provider",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(60)",
            oldMaxLength: 60,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Provider",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Provider",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            table: "Provider",
            type: "nvarchar(200)",
            unicode: false,
            maxLength: 200,
            nullable: true,
            defaultValueSql: "null");

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "Provider",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ServiceName",
            table: "Provider",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            defaultValueSql: "'LEXIS'");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ProjectTag",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ProjectTag",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "StartDate",
            table: "ProjectResource",
            type: "datetime2(3)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2");

        migrationBuilder.AlterColumn<string>(
            name: "PrimaryInvestigatorEmail",
            table: "ProjectResource",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ProjectResource",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "EndDate",
            table: "ProjectResource",
            type: "datetime2(3)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ProjectResource",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            table: "ProjectResource",
            type: "nvarchar(200)",
            unicode: false,
            maxLength: 200,
            nullable: true,
            defaultValueSql: "null");

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "ProjectResource",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ServiceName",
            table: "ProjectResource",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            defaultValueSql: "'LEXIS'");

        migrationBuilder.AlterColumn<DateTime>(
            name: "StartDate",
            table: "Project",
            type: "datetime2(3)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Project",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "EndDate",
            table: "Project",
            type: "datetime2(3)",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2");

        migrationBuilder.AlterColumn<string>(
            name: "Description",
            table: "Project",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(1000)",
            oldMaxLength: 1000,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Project",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AddColumn<string>(
            name: "ExternalId",
            table: "Project",
            type: "nvarchar(200)",
            unicode: false,
            maxLength: 200,
            nullable: true,
            defaultValueSql: "null");

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "Project",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ServiceName",
            table: "Project",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            defaultValueSql: "'LEXIS'");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Location",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Endpoint",
            table: "Location",
            type: "nvarchar(200)",
            unicode: false,
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<string>(
            name: "DefaultServiceIntegrationUrl",
            table: "Location",
            type: "nvarchar(200)",
            unicode: false,
            maxLength: 200,
            nullable: true,
            defaultValueSql: "NULL",
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true,
            oldDefaultValueSql: "NULL");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Location",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "Location",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ServiceName",
            table: "Location",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            defaultValueSql: "'LEXIS'");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ConfigurationValue",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ConfigurationValue",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Configuration",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Configuration",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedUserToProject",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedUserToProject",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedProjectResourceToProject",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedProjectResourceToProject",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "AssignedLocationValue",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedLocation",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedLocation",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "AssignedLocation",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "ServiceName",
            table: "AssignedLocation",
            type: "nvarchar(100)",
            unicode: false,
            maxLength: 100,
            nullable: false,
            defaultValueSql: "'LEXIS'");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ActionLink",
            type: "datetime2(3)",
            nullable: true,
            defaultValueSql: "null",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ActionLink",
            type: "datetime2(3)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2",
            oldDefaultValueSql: "GETUTCDATE()");

        migrationBuilder.AlterColumn<int>(
            name: "Action",
            table: "ActionLink",
            type: "int",
            nullable: false,
            defaultValue: 0,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResource_Name",
            table: "ProjectResource",
            column: "Name");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ProjectResource_Name",
            table: "ProjectResource");

        migrationBuilder.DropColumn(
            name: "ExternalId",
            table: "Provider");

        migrationBuilder.DropColumn(
            name: "Mode",
            table: "Provider");

        migrationBuilder.DropColumn(
            name: "ServiceName",
            table: "Provider");

        migrationBuilder.DropColumn(
            name: "ExternalId",
            table: "ProjectResource");

        migrationBuilder.DropColumn(
            name: "Mode",
            table: "ProjectResource");

        migrationBuilder.DropColumn(
            name: "ServiceName",
            table: "ProjectResource");

        migrationBuilder.DropColumn(
            name: "ExternalId",
            table: "Project");

        migrationBuilder.DropColumn(
            name: "Mode",
            table: "Project");

        migrationBuilder.DropColumn(
            name: "ServiceName",
            table: "Project");

        migrationBuilder.DropColumn(
            name: "Mode",
            table: "Location");

        migrationBuilder.DropColumn(
            name: "ServiceName",
            table: "Location");

        migrationBuilder.DropColumn(
            name: "Mode",
            table: "AssignedLocation");

        migrationBuilder.DropColumn(
            name: "ServiceName",
            table: "AssignedLocation");

        migrationBuilder.RenameIndex(
            name: "UX_User_IdentityProviderUserId",
            table: "User",
            newName: "IX_User_IdentityProviderUserId");

        migrationBuilder.RenameIndex(
            name: "UX_Project_ProjectShortName",
            table: "Project",
            newName: "UK_ProjectShortName");

        migrationBuilder.RenameIndex(
            name: "IX_AssignedLocation_LocationId_ProjectResourceId",
            table: "AssignedLocation",
            newName: "UK_AssignedLocation");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "UserPreference",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<string>(
            name: "Data",
            table: "UserPreference",
            type: "nvarchar(max)",
            maxLength: 5000,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(4000)",
            oldUnicode: false,
            oldMaxLength: 4000);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "UserPreference",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "UserPermission",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "UserPermission",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "User",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "User",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<int>(
            name: "Type",
            table: "Request",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 0);

        migrationBuilder.AlterColumn<int>(
            name: "State",
            table: "Request",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 1);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Request",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Request",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<string>(
            name: "SupportEmail",
            table: "Provider",
            type: "nvarchar(60)",
            maxLength: 60,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldUnicode: false,
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Provider",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Provider",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ProjectTag",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ProjectTag",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "StartDate",
            table: "ProjectResource",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)");

        migrationBuilder.AlterColumn<string>(
            name: "PrimaryInvestigatorEmail",
            table: "ProjectResource",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldUnicode: false,
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ProjectResource",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "EndDate",
            table: "ProjectResource",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ProjectResource",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "StartDate",
            table: "Project",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Project",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "EndDate",
            table: "Project",
            type: "datetime2",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)");

        migrationBuilder.AlterColumn<string>(
            name: "Description",
            table: "Project",
            type: "nvarchar(1000)",
            maxLength: 1000,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(500)",
            oldMaxLength: 500,
            oldNullable: true);

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Project",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Location",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<string>(
            name: "Endpoint",
            table: "Location",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(200)",
            oldUnicode: false,
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "DefaultServiceIntegrationUrl",
            table: "Location",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true,
            defaultValueSql: "NULL",
            oldClrType: typeof(string),
            oldType: "nvarchar(200)",
            oldUnicode: false,
            oldMaxLength: 200,
            oldNullable: true,
            oldDefaultValueSql: "NULL");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Location",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ConfigurationValue",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ConfigurationValue",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "Configuration",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "Configuration",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedUserToProject",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedUserToProject",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedProjectResourceToProject",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedProjectResourceToProject",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "AssignedLocationValue",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldUnicode: false,
            oldMaxLength: 100);

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "AssignedLocation",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "AssignedLocation",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<DateTime>(
            name: "ModifiedAt",
            table: "ActionLink",
            type: "datetime2",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldNullable: true,
            oldDefaultValueSql: "null");

        migrationBuilder.AlterColumn<DateTime>(
            name: "CreatedAt",
            table: "ActionLink",
            type: "datetime2",
            nullable: false,
            defaultValueSql: "GETUTCDATE()",
            oldClrType: typeof(DateTime),
            oldType: "datetime2(3)",
            oldDefaultValueSql: "SYSUTCDATETIME()");

        migrationBuilder.AlterColumn<int>(
            name: "Action",
            table: "ActionLink",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldDefaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResource_Name",
            table: "ProjectResource",
            column: "Name",
            unique: true);
    }
}