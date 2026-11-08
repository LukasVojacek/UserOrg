using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.Migrations
{
    /// <summary>
    /// Reserved id, deliberately empty (ADR-0100).
    ///
    /// This id was applied to live databases by the parallel legacy v6.12.x line, where it dropped the
    /// <c>NewId()</c> DB defaults from the <c>Id</c> column of all 13 dbo Guid primary keys (ADR-0061 —
    /// keys are client-generated UUID v7). The modular line performs the same removals in
    /// <c>20260630191024_ModularCleanup</c>, whose id sorts earlier, so the work is already owned there and
    /// must not be repeated here.
    ///
    /// The id exists in this assembly only so that the applied set in <c>__EFMigrationsHistory</c> is fully
    /// accounted for by the code: without it, databases that came up the legacy line carry a row no migration
    /// explains. Databases that never saw the legacy line apply this as a no-op. Do not give it a body.
    /// </summary>
    public partial class DropGuidIdDefaults : Migration
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