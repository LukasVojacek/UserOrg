using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class Init : Migration
{
    private static readonly string[] columns = ["LocationId", "ProjectResourceId"];
    private static readonly string[] columnsArray = ["Name", "Version"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.CreateTable(
            name: "AllocationUnitType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllocationUnitType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Configuration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Configuration", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ConfigurationType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConfigurationType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Country",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Country", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "LocationType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LocationType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProjectDomainType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectDomainType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ProjectResourceStatus",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectResourceStatus", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SystemModule",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemModule", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "SystemPermissionType",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
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
                IsReserved = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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
            name: "WorkflowExecutionStatus",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
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
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WorkflowType", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Provider",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                IsPrivate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                CountryId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Provider", x => x.Id);
                table.ForeignKey(
                    name: "FK_Provider_Country_CountryId",
                    column: x => x.CountryId,
                    principalTable: "Country",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "ProjectResource",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                PrimaryInvestigatorEmail = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ProjectResourceStatusId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectResource", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectResource_ProjectResourceStatus_ProjectResourceStatusId",
                    column: x => x.ProjectResourceStatusId,
                    principalTable: "ProjectResourceStatus",
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

        migrationBuilder.CreateTable(
            name: "Location",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Endpoint = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsPrivate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationTypeId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Location", x => x.Id);
                table.ForeignKey(
                    name: "FK_Location_LocationType_LocationTypeId",
                    column: x => x.LocationTypeId,
                    principalTable: "LocationType",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Location_Provider_ProviderId",
                    column: x => x.ProviderId,
                    principalTable: "Provider",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "User",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Sid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                UserName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                AssignedProviderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_User", x => x.Id);
                table.ForeignKey(
                    name: "FK_User_Provider_AssignedProviderId",
                    column: x => x.AssignedProviderId,
                    principalTable: "Provider",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AssignedLocation",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AllocationUnitCount = table.Column<long>(type: "bigint", nullable: true),
                AllocationUnitTypeId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedLocation", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssignedLocation_AllocationUnitType_AllocationUnitTypeId",
                    column: x => x.AllocationUnitTypeId,
                    principalTable: "AllocationUnitType",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedLocation_Location_LocationId",
                    column: x => x.LocationId,
                    principalTable: "Location",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedLocation_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "ConfigurationValue",
            columns: table => new
            {
                LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Value = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                ConfigurationTypeId = table.Column<int>(type: "int", nullable: false)
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

        migrationBuilder.CreateTable(
            name: "ConfigurationHistory",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
            name: "Project",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ShortName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ProjectDomainTypeId = table.Column<int>(type: "int", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectConfiguration", x => x.Id);
                table.ForeignKey(
                    name: "FK_Project_ProjectDomainType_ProjectDomainTypeId",
                    column: x => x.ProjectDomainTypeId,
                    principalTable: "ProjectDomainType",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_Project_User_CreatedByUserId",
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
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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

        migrationBuilder.CreateTable(
            name: "ProjectTag",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectTag", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectTag_User_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "UserPreference",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SystemModuleId = table.Column<int>(type: "int", nullable: true),
                Data = table.Column<string>(type: "nvarchar(max)", maxLength: 5000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserPreference", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserPreference_SystemModule_SystemModuleId",
                    column: x => x.SystemModuleId,
                    principalTable: "SystemModule",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserPreference_User_UserId",
                    column: x => x.UserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "WorkflowTemplate",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                WorkflowTypeId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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
            name: "AssignedLocationValue",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                AssignedLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedLocationValue", x => x.Id);
                table.ForeignKey(
                    name: "FK_AssignedLocationValue_AssignedLocation_AssignedLocationId",
                    column: x => x.AssignedLocationId,
                    principalTable: "AssignedLocation",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AssignedProjectResourceToProject",
            columns: table => new
            {
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedProjectResourceToProject", x => new { x.ProjectId, x.ProjectResourceId });
                table.ForeignKey(
                    name: "FK_AssignedProjectResourceToProject_ProjectResource_ProjectResourceId",
                    column: x => x.ProjectResourceId,
                    principalTable: "ProjectResource",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedProjectResourceToProject_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AssignedUserToProject",
            columns: table => new
            {
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedUserToProject", x => new { x.ProjectId, x.UserId });
                table.ForeignKey(
                    name: "FK_AssignedUserToProject_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedUserToProject_User_AssignedByUserId",
                    column: x => x.AssignedByUserId,
                    principalTable: "User",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_AssignedUserToProject_User_UserId",
                    column: x => x.UserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "ProjectHistory",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AttributeName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                OldValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                NewValue = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
            name: "SelfOnboardingRequest",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                State = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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

        migrationBuilder.CreateTable(
            name: "UserPermission",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SystemRoleId = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserPermission", x => x.Id);
                table.ForeignKey(
                    name: "FK_UserPermission_Project_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Project",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserPermission_SystemRole_SystemRoleId",
                    column: x => x.SystemRoleId,
                    principalTable: "SystemRole",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_UserPermission_User_UserId",
                    column: x => x.UserId,
                    principalTable: "User",
                    principalColumn: "Id");
            });

        migrationBuilder.CreateTable(
            name: "AssignedProjectTagToProject",
            columns: table => new
            {
                ProjectTagsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectsId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AssignedProjectTagToProject", x => new { x.ProjectTagsId, x.ProjectsId });
                table.ForeignKey(
                    name: "FK_AssignedProjectTagToProject_ProjectTag_ProjectTagsId",
                    column: x => x.ProjectTagsId,
                    principalTable: "ProjectTag",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AssignedProjectTagToProject_Project_ProjectsId",
                    column: x => x.ProjectsId,
                    principalTable: "Project",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "Workflow",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NewId()"),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                WorkflowTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConfigurationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                ModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OrchestrationId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                StatusId = table.Column<int>(type: "int", nullable: false),
                WorkflowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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
            name: "IX_AssignedLocation_AllocationUnitTypeId",
            table: "AssignedLocation",
            column: "AllocationUnitTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedLocation_ProjectResourceId",
            table: "AssignedLocation",
            column: "ProjectResourceId");

        migrationBuilder.CreateIndex(
            name: "UK_AssignedLocation",
            table: "AssignedLocation",
            columns: columns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AssignedLocationValue_AssignedLocationId",
            table: "AssignedLocationValue",
            column: "AssignedLocationId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedProjectResourceToProject_ProjectResourceId",
            table: "AssignedProjectResourceToProject",
            column: "ProjectResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedProjectTagToProject_ProjectsId",
            table: "AssignedProjectTagToProject",
            column: "ProjectsId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedSystemPermissionTypeToSystemRole_SystemRoleId",
            table: "AssignedSystemPermissionTypeToSystemRole",
            column: "SystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedUserToProject_AssignedByUserId",
            table: "AssignedUserToProject",
            column: "AssignedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedUserToProject_UserId",
            table: "AssignedUserToProject",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationHistory_ConfigurationId",
            table: "ConfigurationHistory",
            column: "ConfigurationId");

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationHistory_CreatedByUserId",
            table: "ConfigurationHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationValue_ConfigurationId",
            table: "ConfigurationValue",
            column: "ConfigurationId");

        migrationBuilder.CreateIndex(
            name: "IX_ConfigurationValue_ConfigurationTypeId",
            table: "ConfigurationValue",
            column: "ConfigurationTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_Location_LocationTypeId",
            table: "Location",
            column: "LocationTypeId");

        migrationBuilder.CreateIndex(
            name: "IX_Location_ProviderId",
            table: "Location",
            column: "ProviderId");

        migrationBuilder.CreateIndex(
            name: "IX_Project_CreatedByUserId",
            table: "Project",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Project_ProjectDomainTypeId",
            table: "Project",
            column: "ProjectDomainTypeId");

        migrationBuilder.CreateIndex(
            name: "UK_ProjectShortName",
            table: "Project",
            column: "ShortName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectHistory_CreatedByUserId",
            table: "ProjectHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectHistory_ProjectId",
            table: "ProjectHistory",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResource_Name",
            table: "ProjectResource",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResource_ProjectResourceStatusId",
            table: "ProjectResource",
            column: "ProjectResourceStatusId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResourceHistory_CreatedByUserId",
            table: "ProjectResourceHistory",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectResourceHistory_ProjectResourceId",
            table: "ProjectResourceHistory",
            column: "ProjectResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_ProjectTag_CreatedByUserId",
            table: "ProjectTag",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Provider_CountryId",
            table: "Provider",
            column: "CountryId");

        migrationBuilder.CreateIndex(
            name: "IX_SelfOnboardingRequest_ProjectId",
            table: "SelfOnboardingRequest",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_SelfOnboardingRequest_UserId",
            table: "SelfOnboardingRequest",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_SystemRole_BaseSystemRoleId",
            table: "SystemRole",
            column: "BaseSystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_SystemRole_Name",
            table: "SystemRole",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_User_AssignedProviderId",
            table: "User",
            column: "AssignedProviderId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPermission_ProjectId",
            table: "UserPermission",
            column: "ProjectId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPermission_SystemRoleId",
            table: "UserPermission",
            column: "SystemRoleId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPermission_UserId",
            table: "UserPermission",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPreference_SystemModuleId",
            table: "UserPreference",
            column: "SystemModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_UserPreference_UserId",
            table: "UserPreference",
            column: "UserId");

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
            columns: columnsArray,
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(
            name: "AssignedLocationValue");

        migrationBuilder.DropTable(
            name: "AssignedProjectResourceToProject");

        migrationBuilder.DropTable(
            name: "AssignedProjectTagToProject");

        migrationBuilder.DropTable(
            name: "AssignedSystemPermissionTypeToSystemRole");

        migrationBuilder.DropTable(
            name: "AssignedUserToProject");

        migrationBuilder.DropTable(
            name: "ConfigurationHistory");

        migrationBuilder.DropTable(
            name: "ConfigurationValue");

        migrationBuilder.DropTable(
            name: "ProjectHistory");

        migrationBuilder.DropTable(
            name: "ProjectResourceHistory");

        migrationBuilder.DropTable(
            name: "SelfOnboardingRequest");

        migrationBuilder.DropTable(
            name: "UserPermission");

        migrationBuilder.DropTable(
            name: "UserPreference");

        migrationBuilder.DropTable(
            name: "WorkflowExecution");

        migrationBuilder.DropTable(
            name: "AssignedLocation");

        migrationBuilder.DropTable(
            name: "ProjectTag");

        migrationBuilder.DropTable(
            name: "SystemPermissionType");

        migrationBuilder.DropTable(
            name: "ConfigurationType");

        migrationBuilder.DropTable(
            name: "SystemRole");

        migrationBuilder.DropTable(
            name: "SystemModule");

        migrationBuilder.DropTable(
            name: "WorkflowExecutionStatus");

        migrationBuilder.DropTable(
            name: "Workflow");

        migrationBuilder.DropTable(
            name: "AllocationUnitType");

        migrationBuilder.DropTable(
            name: "Location");

        migrationBuilder.DropTable(
            name: "ProjectResource");

        migrationBuilder.DropTable(
            name: "Configuration");

        migrationBuilder.DropTable(
            name: "Project");

        migrationBuilder.DropTable(
            name: "WorkflowTemplate");

        migrationBuilder.DropTable(
            name: "LocationType");

        migrationBuilder.DropTable(
            name: "ProjectResourceStatus");

        migrationBuilder.DropTable(
            name: "ProjectDomainType");

        migrationBuilder.DropTable(
            name: "User");

        migrationBuilder.DropTable(
            name: "WorkflowType");

        migrationBuilder.DropTable(
            name: "Provider");

        migrationBuilder.DropTable(
            name: "Country");
    }
}