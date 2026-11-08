using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.Migrations
{
    /// <summary>
    /// DEC-6: the draft resource concept is gone (see `2cb81fd3`); <c>Availability</c> has had no reader or
    /// writer since B05. Irreversible per DEC-6 only if no live row is <c>Draft</c> (1) —
    /// <c>SELECT COUNT(*) FROM ProjectResource WHERE Availability &lt;&gt; 0</c> must return 0 on every target
    /// database before this migration runs; this was NOT verified against a live database when authored
    /// (no reachable SQL instance in that environment) and must be checked before deploy.
    /// </summary>
    public partial class DropProjectResourceAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PRECONDITION (DEC-6), enforced below rather than trusted: no row may be Draft (Availability 1).
            // Nobody ran the check by hand — no SQL instance was reachable when this was authored — so the
            // migration checks itself. Availability 1 = Draft (ADR-0065); dropping the column erases the
            // Draft/Active distinction permanently and Down() cannot bring those values back, so a silent
            // drop is unacceptable. EF runs each SQL Server migration in a transaction: the THROW aborts and
            // rolls back, leaving the column and its data intact. If it fires, STOP and take the count back
            // to DEC-6 for a re-decision (migrate the Draft rows first, or keep the column).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [dbo].[ProjectResource] WHERE [Availability] <> 0)
BEGIN
    THROW 51000, 'DEC-6 precondition failed: [dbo].[ProjectResource] contains rows with Availability <> 0 (Draft). Dropping the column would erase them irreversibly. Resolve those rows, then re-run this migration.', 1;
END");

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "ProjectResource");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the column's shape only, not its data: every row comes back as 0 (Active).
            // Any Draft (1) value dropped by Up() is unrecoverable, so this is not a true inverse.
            migrationBuilder.AddColumn<int>(
                name: "Availability",
                table: "ProjectResource",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }
    }
}