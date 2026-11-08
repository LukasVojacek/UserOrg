using Heappe.Contracts;
using Heappe.Contracts.Connection;
using Heappe.Contracts.Entries.Sync;
using Heappe.Features.LogRetrieval;
using Heappe.HostContracts;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using UserOrg.Shared;
using UserOrg.Shared.Persistance.Abstractions.Enums;

using Xunit;

namespace Heappe.UnitTests.Features.LogRetrieval;

/// <summary>
/// Unit tests for <see cref="DownloadChangedFilesEndpoint"/>. Behaviour is verified through the
/// handler with the ports (<see cref="IUserorgService"/>, <see cref="IHeappeConnectionFactory"/>,
/// <see cref="IHeappeService"/>) mocked.
/// </summary>
public sealed class DownloadChangedFilesEndpointTests
{
    private static readonly Uri HeappeInstanceUri = new("https://heappe.example.org/");
    private static readonly HeappeIdentifier FirstIdentifier = new("id-a");
    private static readonly HeappeIdentifier SecondIdentifier = new("id-b");

    private readonly Mock<IUserorgService> _userorg = new(MockBehavior.Strict);
    private readonly Mock<IHeappeConnectionFactory> _connections = new(MockBehavior.Strict);
    private readonly Mock<IHeappeService> _heappe = new(MockBehavior.Strict);

    private static DownloadChangedFilesEndpoint.Request NewRequest() =>
        new("projA", "RES-1", 42L);

    private static Error SomeError(string description) =>
        new ErrorBuilder().WithDescription(description).WithSeverity(ErrorSeverity.Error).Build();

    private static ProjectResourceInfoSyncEntry Allocation(params LocationAllocationInfoSyncEntry[] locations) =>
        new(
            Guid.NewGuid(),
            "Resource One",
            "RES-1",
            null,
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            new ResourceInfoSyncEntry(Guid.NewGuid(), "RES-1", null, null, null, locations));

    private static LocationAllocationInfoSyncEntry Location(string locationName, HeappeIdentifier identifier) =>
        new(
            Guid.NewGuid(),
            locationName,
            "login.example.org",
            ELocationType.HPC,
            null,
            new HeappeInstanceSyncEntry(HeappeInstanceUri, new Version(4, 3), identifier));

    private void SetupUserConnection(HeappeIdentifier identifier) =>
        _connections
            .Setup(f => f.ForUserAsync(identifier, "token", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Success(new PublicHeappeConnectionInfo(HeappeInstanceUri, identifier), State.Ok));

    private Task<IResult> Invoke(OriginalBearerToken? token) =>
        DownloadChangedFilesEndpoint.HandleAsync(
            NewRequest(), token, _userorg.Object, _connections.Object, _heappe.Object, NullLoggerFactory.Instance, CancellationToken.None);

    [Fact]
    public async Task Should_Return_Unauthorized_When_Token_Is_Null()
    {
        var result = await Invoke(token: null);

        Assert.IsType<UnauthorizedHttpResult>(result);
    }

    [Fact]
    public async Task Should_Return_NotFound_When_Allocation_Lookup_Fails()
    {
        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Failure(SomeError("not here"), State.NotFound));

        var result = await Invoke(new OriginalBearerToken("token"));

        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Should_Return_BadRequest_When_No_Location_Carries_A_HeappeIdentifier()
    {
        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(Allocation(), State.Ok));

        var result = await Invoke(new OriginalBearerToken("token"));

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task Should_Return_Ok_With_Files_When_Download_Succeeds()
    {
        var files = new Dictionary<string, DownloadedFileModel>
        {
            ["out.log"] = new DownloadedFileModel("out.log", "content")
        };

        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(Allocation(Location("loc", FirstIdentifier)), State.Ok));
        SetupUserConnection(FirstIdentifier);
        _heappe
            .Setup(s => s.DownloadChangedFilesAsync(It.IsAny<BaseHeappeConnectionInfo>(), 42L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Dictionary<string, DownloadedFileModel>, Error>.Success(files, State.Ok));

        var result = await Invoke(new OriginalBearerToken("token"));

        var ok = Assert.IsType<Ok<Dictionary<string, DownloadedFileModel>>>(result);
        Assert.Same(files, ok.Value);
    }

    [Fact]
    public async Task Should_Return_InternalServerError_When_Download_Fails()
    {
        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(Allocation(Location("loc", FirstIdentifier)), State.Ok));
        SetupUserConnection(FirstIdentifier);
        _heappe
            .Setup(s => s.DownloadChangedFilesAsync(It.IsAny<BaseHeappeConnectionInfo>(), 42L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Dictionary<string, DownloadedFileModel>, Error>.Failure(SomeError("boom"), State.Error));

        var result = await Invoke(new OriginalBearerToken("token"));

        var problem = Assert.IsType<InternalServerError<Microsoft.AspNetCore.Mvc.ProblemDetails>>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.StatusCode);
    }

    [Fact]
    public async Task Should_Query_The_Instance_Of_The_First_Location_By_Name_When_Several_Carry_An_Identifier()
    {
        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(
                Allocation(Location("b", SecondIdentifier), Location("a", FirstIdentifier)), State.Ok));
        SetupUserConnection(FirstIdentifier);
        _heappe
            .Setup(s => s.DownloadChangedFilesAsync(It.Is<BaseHeappeConnectionInfo>(c => c.Identifier == FirstIdentifier), 42L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Dictionary<string, DownloadedFileModel>, Error>.Success([], State.Ok));

        var result = await Invoke(new OriginalBearerToken("token"));

        Assert.IsType<Ok<Dictionary<string, DownloadedFileModel>>>(result);
        _connections.Verify(f => f.ForUserAsync(SecondIdentifier, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_Return_InternalServerError_When_The_Instance_Cannot_Be_Resolved()
    {
        _userorg
            .Setup(s => s.GetProjectResourceAllocationWithLocationsAsync("projA", "RES-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ProjectResourceInfoSyncEntry, Error>.Success(Allocation(Location("loc", FirstIdentifier)), State.Ok));
        _connections
            .Setup(f => f.ForUserAsync(FirstIdentifier, "token", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<BaseHeappeConnectionInfo, Error>.Failure(SomeError("not listed"), State.Error));

        var result = await Invoke(new OriginalBearerToken("token"));

        Assert.IsType<InternalServerError<Microsoft.AspNetCore.Mvc.ProblemDetails>>(result);
    }
}
