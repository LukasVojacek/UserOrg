using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.Migrations
{
    /// <summary>
    /// Reserved id, deliberately empty (ADR-0100).
    ///
    /// This id was applied to live databases by the parallel legacy v6.12.x line, where it dropped
    /// <c>UserPreference.SystemModuleId</c> together with <c>IX_UserPreference_SystemModuleId</c> and
    /// <c>FK_UserPreference_SystemModule_SystemModuleId</c>. The modular line performs the same removals in
    /// <c>20260630191024_ModularCleanup</c>, whose id sorts earlier, so the work is already owned there and
    /// must not be repeated here.
    ///
    /// The id exists in this assembly only so that the applied set in <c>__EFMigrationsHistory</c> is fully
    /// accounted for by the code: without it, databases that came up the legacy line carry a row no migration
    /// explains. Databases that never saw the legacy line apply this as a no-op. Do not give it a body.
    /// </summary>
    public partial class SyncPendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}