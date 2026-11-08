using Heappe.Features.GetLocationPartitions;
using Heappe.Features.GetLocationPartitions.Response;
using Heappe.HostContracts;
using Heappe.Persistence;
using Heappe.Persistence.Entities;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

using Moq;

using UserOrg.Shared;

using Xunit;

using Error = Lukdrasil.Error.Error;

namespace Heappe.UnitTests.Features.GetLocationPartitions;

public sealed class GetPartitionsEndpointTests
{
    private static readonly Guid _locationId = Guid.Parse("33333333-0000-0000-0000-000000000000");
    private static readonly Guid _otherLocationId = Guid.Parse("44444444-0000-0000-0000-000000000000");
    private const string ProjectShortName = "tp";
    private const string ResourceName = "res1";
    private const string Aggregation = "BARBORA CPU";

    [Fact(DisplayName = "Partitions: partitions of a different location or aggregation are filtered out")]
    public async Task Handle_Should_FilterOutIrrelevantPartitions()
    {
        var db = CreateDbContext(
            NewPartition("qcpu_exp", Aggregation, _locationId),
            NewPartition("other", "OTHER AGG", _locationId),
            NewPartition("foreign", Aggregation, _otherLocationId));
        var service = CreateService(new ProviderLocationInfo(_locationId, "Barbora", Aggregation, "IT4I"));

        var data = await ExecuteAsync(service, db);

        var item = Assert.Single(data);
        Assert.Equal("qcpu_exp", item.PartitionName);
    }

    [Fact(DisplayName = "Partitions: exact (LocationId, AggregationName) pair is required, cross combinations are not returned")]
    public async Task Handle_Should_NotReturnCrossCombinations()
    {
        var db = CreateDbContext(
            NewPartition("p1", "AGG1", _locationId),
            NewPartition("p2", "AGG2", _otherLocationId),
            NewPartition("cross1", "AGG2", _locationId),
            NewPartition("cross2", "AGG1", _otherLocationId));
        var service = CreateService(
            new ProviderLocationInfo(_locationId, "Loc1", "AGG1", "IT4I"),
            new ProviderLocationInfo(_otherLocationId, "Loc2", "AGG2", "IT4I"));

        var data = await ExecuteAsync(service, db);

        Assert.Equal(["p1", "p2"], data.Select(d => d.PartitionName).Order().ToArray());
    }

    [Fact(DisplayName = "Partitions: null aggregation on location matches partition with empty or null aggregation")]
    public async Task Handle_Should_MatchNullAggregationWithEmpty()
    {
        var db = CreateDbContext(NewPartition("p1", null, _locationId));
        var service = CreateService(new ProviderLocationInfo(_locationId, "Barbora", null, "IT4I"));

        var data = await ExecuteAsync(service, db);

        var item = Assert.Single(data);
        Assert.Equal(string.Empty, item.AggregationName);
    }

    [Fact(DisplayName = "Partitions: 404 when location lookup fails")]
    public async Task Handle_Should_ReturnNotFound_When_LookupFails()
    {
        var db = CreateDbContext();
        var error = new ErrorBuilder()
            .WithDescription("not found")
            .WithSeverity(ErrorSeverity.Error)
            .Build();
        var service = new Mock<IUserorgService>(MockBehavior.Strict);
        service
            .Setup(s => s.GetValidLocationNames(ProjectShortName, ResourceName, It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<ProviderLocationInfo>, Error>.Failure(error, State.NotFound));

        var result = await GetPartitionsEndpoint.HandleAsync(
            new GetPartitionsEndpoint.Request(ProjectShortName, ResourceName),
            service.Object,
            db,
            CancellationToken.None);

        Assert.IsType<NotFound>(result);
    }

    private static async Task<List<LocationPartitionResponse>> ExecuteAsync(Mock<IUserorgService> service, HeappeIntegrationDbContext db)
    {
        var result = await GetPartitionsEndpoint.HandleAsync(
            new GetPartitionsEndpoint.Request(ProjectShortName, ResourceName),
            service.Object,
            db,
            CancellationToken.None);

        return Assert.IsType<Ok<List<LocationPartitionResponse>>>(result).Value!;
    }

    private static Mock<IUserorgService> CreateService(params ProviderLocationInfo[] locations)
    {
        var service = new Mock<IUserorgService>(MockBehavior.Strict);
        service
            .Setup(s => s.GetValidLocationNames(ProjectShortName, ResourceName, It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<HashSet<ProviderLocationInfo>, Error>.Success(locations.ToHashSet(), State.Ok));
        return service;
    }

    private static LocationPartition NewPartition(string name, string? aggregation, Guid locationId) =>
        new() { Name = name, AggregationName = aggregation, LocationId = locationId };

    private static HeappeIntegrationDbContext CreateDbContext(params LocationPartition[] partitions)
    {
        var options = new DbContextOptionsBuilder<HeappeIntegrationDbContext>()
            .UseInMemoryDatabase($"Partitions_{Guid.NewGuid()}")
            .Options;
        var db = new HeappeIntegrationDbContext(options);
        db.LocationPartitions.AddRange(partitions);
        db.SaveChanges();
        return db;
    }
}
