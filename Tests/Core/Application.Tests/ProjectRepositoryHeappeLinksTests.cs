using Microsoft.EntityFrameworkCore;

using UserOrg.Shared;

using UserOrganizationManagement.Domain.ExtModels.UserOrg.OutputModels.Project;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.ValueObjects;
using UserOrganizationManagement.Infrastructure.DataAccess.Contexts;
using UserOrganizationManagement.Infrastructure.DataAccess.Repositories;

namespace UserOrganizationManagement.Application.Tests;

/// <summary>
/// <see cref="ProjectRepository.GetHeappeLinks"/>: every HEAppE call is routed through NexusApi by the
/// <c>HEAPPE_IDENTIFIER</c> of the location (#349). The URL is kept for logs only, so a location with a URL and no
/// identifier is listed without one and the caller skips it.
/// </summary>
public sealed class ProjectRepositoryHeappeLinksTests
{
    private const string ShortName = "HEAPPELINKS";

    private static DatabaseContext BuildContext() => new(
        new DbContextOptionsBuilder<DatabaseContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact(DisplayName = "A location with a HEAPPE_IDENTIFIER yields the normalised identifier and keeps the URL for logs")]
    public async Task YieldsTheIdentifierOfTheLocation()
    {
        using var context = BuildContext();
        context.Projects.Add(ProjectWithLocationValues(
            ("HEAPPE_URL", "https://heappe.example.org"),
            ("HEAPPE_IDENTIFIER", " HEAppE-A ")));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var links = await new ProjectRepository(context).GetHeappeLinks(new ProjectShortName(ShortName), TestContext.Current.CancellationToken);

        var link = Assert.IsType<ProjectHEAppELinks>(Assert.Single(links));
        Assert.Equal(new HeappeIdentifier("heappe-a"), link.HEAppEApiIdentifier);
        Assert.NotNull(link.HEAppEApi);
    }

    [Fact(DisplayName = "A location with only a HEAPPE_URL is listed without an identifier, so it cannot be routed")]
    public async Task YieldsNoIdentifierForAUrlOnlyLocation()
    {
        using var context = BuildContext();
        context.Projects.Add(ProjectWithLocationValues(("HEAPPE_URL", "https://heappe.example.org")));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var links = await new ProjectRepository(context).GetHeappeLinks(new ProjectShortName(ShortName), TestContext.Current.CancellationToken);

        var link = Assert.IsType<ProjectHEAppELinks>(Assert.Single(links));
        Assert.Null(link.HEAppEApiIdentifier);
    }

    [Fact(DisplayName = "A location with an identifier that is not a valid one is listed without it")]
    public async Task YieldsNoIdentifierForAnInvalidOne()
    {
        using var context = BuildContext();
        context.Projects.Add(ProjectWithLocationValues(("HEAPPE_IDENTIFIER", "not a valid identifier!")));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var links = await new ProjectRepository(context).GetHeappeLinks(new ProjectShortName(ShortName), TestContext.Current.CancellationToken);

        Assert.Null(Assert.IsType<ProjectHEAppELinks>(Assert.Single(links)).HEAppEApiIdentifier);
    }

    private static Project ProjectWithLocationValues(params (string Name, string Value)[] values)
    {
        var country = new Country { Id = 1, Name = "CZ" };
        var provider = new Provider { Name = "Provider", Country = country };
        var location = new Location
        {
            Name = "Location",
            Endpoint = "https://location.example",
            Provider = provider,
            LocationType = new LocationType { Id = 1, Name = "HPC" },
        };
        var resource = new ProjectResource
        {
            Name = "Resource",
            StartDate = DateTime.UtcNow.AddDays(-365),
            EndDate = DateTime.UtcNow.AddDays(30),
        };
        var assignedLocation = new AssignedLocation
        {
            ProjectResource = resource,
            Location = location,
            AllocationUnitType = new AllocationUnitType { Id = 1, Name = "CoreHours" },
        };
        foreach (var (name, value) in values)
        {
            assignedLocation.AssignedLocationsValues.Add(new AssignedLocationValue { Name = name, Value = value, AssignedLocation = assignedLocation });
        }

        resource.AssignedLocations.Add(assignedLocation);
        location.AssignedLocations.Add(assignedLocation);

        var project = new Project
        {
            ShortName = new ProjectShortName(ShortName),
            Name = ShortName,
            ProjectDomainType = new ProjectDomainType { Id = 1, Name = "Domain" },
            CreatedByUser = new User { Email = "owner@example.com" },
            StartDate = DateTime.UtcNow.AddDays(-365),
            EndDate = DateTime.UtcNow.AddDays(30),
        };
        project.AssignedProjectResources.Add(new AssignedProjectResourceToProject { Project = project, ProjectResource = resource });
        return project;
    }
}
