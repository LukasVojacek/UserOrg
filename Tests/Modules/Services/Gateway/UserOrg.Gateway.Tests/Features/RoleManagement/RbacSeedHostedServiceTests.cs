using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.Extensions.DependencyInjection;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests.Features.RoleManagement;

public sealed class RbacSeedHostedServiceTests
{
    private static IServiceScopeFactory CreateScopeFactory(IRoleManagementService service)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRoleManagementService>(_ => service);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static RbacSeedHostedService CreateSut(
        IRoleManagementService service,
        bool fileExists = true,
        Stream? seedStream = null,
        string seedFilePath = "/test/seed.json")
    {
        var scopeFactory = CreateScopeFactory(service);
        var options = new RoleManagementOptions { SeedFilePath = seedFilePath };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RbacSeedHostedService>.Instance;
        return new RbacSeedHostedService(scopeFactory, options, logger)
        {
            FileExists = _ => fileExists,
            OpenRead = _ => seedStream ?? new MemoryStream()
        };
    }

    private static Mock<IRoleManagementService> ServiceWithImport(ImportExportSummary summary)
    {
        var mock = new Mock<IRoleManagementService>();
        mock
            .Setup(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ImportExportSummary, Error>.Success(summary));
        mock
            .Setup(s => s.InvalidateEndpointPermissionCacheAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task StartAsync_WhenSeedFileDoesNotExist_DoesNotCallService()
    {
        var mockService = new Mock<IRoleManagementService>();
        var sut = CreateSut(mockService.Object, fileExists: false);

        await sut.StartAsync(CancellationToken.None);

        mockService.Verify(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        mockService.Verify(s => s.InvalidateEndpointPermissionCacheAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenDbHasExistingRoles_StillRunsAdditiveImport()
    {
        // Behavior change (ADR-0060): reconciliation runs on every startup, no longer skipped when roles exist.
        var mockService = ServiceWithImport(new ImportExportSummary(true, CreatedCount: 0));

        var sut = CreateSut(mockService.Object);

        await sut.StartAsync(CancellationToken.None);

        mockService.Verify(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_AlwaysImportsWithOverwriteExistingFalse()
    {
        bool? capturedOverwrite = null;
        var mockService = new Mock<IRoleManagementService>();
        mockService
            .Setup(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, bool, CancellationToken>((_, overwrite, _) => capturedOverwrite = overwrite)
            .ReturnsAsync(Result<ImportExportSummary, Error>.Success(new ImportExportSummary(true)));

        var sut = CreateSut(mockService.Object);

        await sut.StartAsync(CancellationToken.None);

        Assert.False(capturedOverwrite);
    }

    [Fact]
    public async Task StartAsync_WhenInsertionsOccur_InvalidatesSnapshotCacheOnce()
    {
        var mockService = ServiceWithImport(new ImportExportSummary(true, CreatedCount: 3));

        var sut = CreateSut(mockService.Object);

        await sut.StartAsync(CancellationToken.None);

        mockService.Verify(s => s.InvalidateEndpointPermissionCacheAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartAsync_WhenNothingInserted_DoesNotInvalidateSnapshotCache()
    {
        var mockService = ServiceWithImport(new ImportExportSummary(true, CreatedCount: 0));

        var sut = CreateSut(mockService.Object);

        await sut.StartAsync(CancellationToken.None);

        mockService.Verify(s => s.InvalidateEndpointPermissionCacheAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenImportReturnsFailure_DoesNotThrowAndDoesNotInvalidate()
    {
        var mockService = new Mock<IRoleManagementService>();
        mockService
            .Setup(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ImportExportSummary, Error>.Failure(new Error("seed.failed", ErrorSeverity.Error, "Import failed")));

        var sut = CreateSut(mockService.Object);

        var exception = await Record.ExceptionAsync(() => sut.StartAsync(CancellationToken.None));

        Assert.Null(exception);
        mockService.Verify(s => s.InvalidateEndpointPermissionCacheAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_WhenImportThrows_DoesNotThrow()
    {
        var mockService = new Mock<IRoleManagementService>();
        mockService
            .Setup(s => s.ImportRolesAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var sut = CreateSut(mockService.Object);

        var exception = await Record.ExceptionAsync(() => sut.StartAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_UsesPathFromOptions()
    {
        const string customPath = "/custom/rbac/seed.json";
        string? capturedPath = null;

        var mockService = ServiceWithImport(new ImportExportSummary(true));

        var scopeFactory = CreateScopeFactory(mockService.Object);
        var options = new RoleManagementOptions { SeedFilePath = customPath };
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<RbacSeedHostedService>.Instance;
        var sut = new RbacSeedHostedService(scopeFactory, options, logger)
        {
            FileExists = path => { capturedPath = path; return true; },
            OpenRead = _ => new MemoryStream()
        };

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(customPath, capturedPath);
    }

    [Fact]
    public async Task StopAsync_AlwaysCompletesImmediately()
    {
        var sut = CreateSut(Mock.Of<IRoleManagementService>());

        var exception = await Record.ExceptionAsync(() => sut.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
