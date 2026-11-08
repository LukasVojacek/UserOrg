using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Core.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class UniqueLookupNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases can already hold several rows per name (the Waldur storage reconcile inserted
            // Country "FI" inline), so the references move to the lowest Id of each name and the other rows go
            // before the unique indexes are created.
            migrationBuilder.Sql(@"
UPDATE p SET p.CountryId = k.KeepId
FROM Provider p
JOIN Country c ON c.Id = p.CountryId
JOIN (SELECT Name, MIN(Id) AS KeepId FROM Country GROUP BY Name) k ON k.Name = c.Name
WHERE p.CountryId <> k.KeepId;

DELETE c FROM Country c
WHERE c.Id <> (SELECT MIN(k.Id) FROM Country k WHERE k.Name = c.Name);

UPDATE p SET p.ProjectDomainTypeId = k.KeepId
FROM Project p
JOIN ProjectDomainType d ON d.Id = p.ProjectDomainTypeId
JOIN (SELECT Name, MIN(Id) AS KeepId FROM ProjectDomainType GROUP BY Name) k ON k.Name = d.Name
WHERE p.ProjectDomainTypeId <> k.KeepId;

DELETE d FROM ProjectDomainType d
WHERE d.Id <> (SELECT MIN(k.Id) FROM ProjectDomainType k WHERE k.Name = d.Name);");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDomainType_Name",
                table: "ProjectDomainType",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Country_Name",
                table: "Country",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectDomainType_Name",
                table: "ProjectDomainType");

            migrationBuilder.DropIndex(
                name: "IX_Country_Name",
                table: "Country");
        }
    }
}