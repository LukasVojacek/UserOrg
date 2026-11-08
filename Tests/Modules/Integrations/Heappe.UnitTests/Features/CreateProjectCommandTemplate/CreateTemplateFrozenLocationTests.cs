using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries;
using Heappe.Contracts.Entries.Sync;
using Heappe.Features.CreateProjectCommandTemplate;
using Heappe.Features.CreateProjectCommandTemplate.Request;
using Heappe.Features.Credentials;
using Heappe.Features.KillSwitch;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using UserOrg.Shared;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.CreateProjectCommandTemplate;

/// <summary>
/// Pins the B07 write-guard decision (ADR-0077 §5) on the Create flow. Create acts on a Location
/// BEFORE any <c>ProjectCommandTemplateId</c> exists (LOCATION-grain guard, ADR-0077 §5/B02-shipped
/// notes): a frozen target Location rejects with 409 before HEAppE, credentials, or partition
/// lookups are even attempted — never a template is created against a compromised location.
/// </summary>
public sealed class CreateTemplateFrozenLocationTests
{
    private static readonly Guid _projectId = Guid.Parse("11111111-0000-0000-0000-000000000000");
    private static readonly Guid _resourceId = Guid.Parse("22222222-0000-0000-0000-000000000000");
    private static readonly Guid _locationId = Guid.Parse("33333333-0000-0000-0000-000000000000");
    private const string ProjectShortName = "tp";
    private const string ResourceName = "res1";
    private const string LocationName = "Karolina";
    private const string ReasonText = "Suspected RCE in BulkCopy - do not disclose";

    [Fact(DisplayName = "Create: 409 (not 500) when the target location is frozen, message excludes reason, no downstream calls made")]
    public async Task Create_Should_Return409_And_NotCallDownstreamServices_When_LocationIsFrozen()
    {
        var dbContext = CreateDbContextWithFrozenLocation();

        var userorgService = new Mock<IUserorgService>(MockBehavior.Strict);
        userorgService
            .Setup(s => s.GetProjectResourceAllocationAsync(ProjectShortName, ResourceName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoEntry, Error>.Success(
                new ProjectResourceInfoEntry(
                    _projectId, "Project", ProjectShortName, null,
                    DateTime.UtcNow, DateTime.UtcNow.AddYears(1),
                    new ResourceInfoEntry(_resourceId, ResourceName, null, null, null)),
                State.Ok));
        userorgService
            .Setup(s => s.GetValidLocationNames(
                ProjectShortName, ResourceName, It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<ProviderLocationInfo>, Error>.Success(
                [new ProviderLocationInfo(_locationId, LocationName, null, "prov1")],
                State.Ok));
        // Deliberately NO setup for GetProjectResourceAllocationWithLocationsAsync — the guard must
        // short-circuit before this call (Strict mock throws if it is reached).

        var writeGuard = new CommandTemplateWriteGuard(new KillSwitchStateService(dbContext), dbContext);

        var result = await CreateTemplateEndpoint.HandleAsync(
            CreateRequest(),
            userorgService.Object,
            dbContext,
            Mock.Of<IHeappeService>(MockBehavior.Strict),
            CreateLoggerFactory(),
            Mock.Of<IHeappeConnectionFactory>(MockBehavior.Strict),
            Mock.Of<IProjectCommandTemplateCreateService>(MockBehavior.Strict),
            new FakeHybridCache(),
            writeGuard,
            CancellationToken.None);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        Assert.Contains(LocationName, problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("RCE", problem.ProblemDetails.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("BulkCopy", problem.ProblemDetails.Detail, StringComparison.Ordinal);
    }

    private static CreateTemplateRequest CreateRequest() =>
        new(
            ProjectIdentifier: new ProjectResourceTarget(ProjectShortName, ResourceName),
            PartitionName: "queue1",
            LocationName: LocationName,
            AggregationName: null,
            Name: "MyTemplate",
            Description: null,
            PreparationScriptPath: null,
            ExecutionScriptPath: "/scripts/run.sh",
            CommandTemplateParameters: []);

    private static HeappeIntegrationDbContext CreateDbContextWithFrozenLocation()
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"CreateFrozen_{Guid.NewGuid()}")
            .Options;
        var dbContext = new HeappeIntegrationDbContext(options);

        dbContext.LocationCommandTemplateKillSwitches.Add(new LocationCommandTemplateKillSwitch
        {
            LocationId = _locationId,
            IsEnforced = true,
            Reason = ReasonText,
            EnforcedAtUtc = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            EnforcedByUserId = Guid.NewGuid()
        });
        dbContext.SaveChanges();

        return dbContext;
    }

    private static ILoggerFactory CreateLoggerFactory()
    {
        var factory = new Mock<ILoggerFactory>(MockBehavior.Loose);
        factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        return factory.Object;
    }
}
