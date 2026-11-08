using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserOrganizationManagement.Infrastructure.DataAccess.Migrations;

/// <inheritdoc />
public partial class AddIndexsToUserAndAssignedUserToProject : Migration
{
    private static readonly string[] value = new[] { "AssignedProviderId", "Id" };
    private static readonly string[] columns = new[] { "UserId", "ProjectId" };
    private static readonly string[] valueArray = new[] { "AssignedByUserId", "CreatedAt", "IsDeleted", "ModifiedAt" };

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop index if it exists to avoid errors when running migrations against databases
        // that may be in different states or when permissions differ.
        migrationBuilder.Sql(@"IF EXISTS (
    SELECT 1 FROM sys.indexes i
    INNER JOIN sys.objects o ON i.object_id = o.object_id
    WHERE i.name = 'IX_User_IdentityProviderUserId' AND o.name = 'User')
BEGIN
    DROP INDEX IX_User_IdentityProviderUserId ON [User];
END");

        migrationBuilder.CreateIndex(
            name: "IX_User_IdentityProviderUserId",
            table: "User",
            column: "IdentityProviderUserId",
            unique: true)
            .Annotation("SqlServer:Include", value);

        migrationBuilder.Sql(@"IF EXISTS (
    SELECT 1 FROM sys.indexes i
    INNER JOIN sys.objects o ON i.object_id = o.object_id
    WHERE i.name = 'IX_AssignedUserToProject_UserId_ProjectId' AND o.name = 'AssignedUserToProject')
BEGIN
    DROP INDEX IX_AssignedUserToProject_UserId_ProjectId ON AssignedUserToProject;
END");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedUserToProject_UserId_ProjectId",
            table: "AssignedUserToProject",
            columns: columns)
            .Annotation("SqlServer:Include", valueArray);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (
    SELECT 1 FROM sys.indexes i
    INNER JOIN sys.objects o ON i.object_id = o.object_id
    WHERE i.name = 'IX_User_IdentityProviderUserId' AND o.name = 'User')
BEGIN
    DROP INDEX IX_User_IdentityProviderUserId ON [User];
END");

        migrationBuilder.Sql(@"IF EXISTS (
    SELECT 1 FROM sys.indexes i
    INNER JOIN sys.objects o ON i.object_id = o.object_id
    WHERE i.name = 'IX_AssignedUserToProject_UserId_ProjectId' AND o.name = 'AssignedUserToProject')
BEGIN
    DROP INDEX IX_AssignedUserToProject_UserId_ProjectId ON AssignedUserToProject;
END");

        migrationBuilder.CreateIndex(
            name: "IX_AssignedUserToProject_UserId",
            table: "AssignedUserToProject",
            column: "UserId");
    }
}