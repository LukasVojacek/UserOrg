using System.Reflection;
using System.Text.Json;

using Forge.Core.Features.EndpointMap;
using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Persistence;

using Lukdrasil.Error;
using Lukdrasil.Result;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using UserOrg.Shared;

namespace Forge.Core.Tests.Features.EndpointMap;

/// <summary>
/// Characterization tests pinning today's parse/import behaviour of
/// <see cref="EndpointMappingImportExport"/>, end-to-end through the public
/// <c>ImportAsync(JsonElement, ...)</c> API, plus the snapshot invalidation the importer owes
/// every import (ADR-0084, issue #210).
/// </summary>
public sealed class EndpointMappingImportExportTests : IDisposable
{
    private readonly ForgeDbContext _context;
    private readonly IDbContextFactory<ForgeDbContext> _contextFactory;
    private readonly RecordingEndpointPermissionService _permissionService;
    private readonly EndpointMappingImportExport _importExport;

    public EndpointMappingImportExportTests()
    {
        var options = new DbContextOptionsBuilder<ForgeDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ForgeDbContext(options);
        _contextFactory = new TestDbContextFactory(options);
        _permissionService = new RecordingEndpointPermissionService();
        _importExport = new EndpointMappingImportExport(
            _contextFactory,
            _permissionService,
            NullLogger<EndpointMappingImportExport>.Instance);
    }

    /// <summary>
    /// Invokes the typed import overload, which <see cref="TypedForgeImportExport{TPayload}"/>
    /// declares <c>protected</c> so that the invalidating <c>ImportAsync(JsonElement, ...)</c>
    /// stays the only reachable entry point. Reflection keeps the pre-existing characterization
    /// assertions on this overload intact.
    /// </summary>
    private Task<ImportResult> ImportTypedAsync(EndpointMappingImportDto payload, ImportOptions options)
    {
        var method = typeof(EndpointMappingImportExport).GetMethod(
            "ImportAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(EndpointMappingImportDto), typeof(ImportOptions), typeof(CancellationToken)]);

        Assert.NotNull(method);

        return (Task<ImportResult>)method.Invoke(_importExport, [payload, options, CancellationToken.None])!;
    }

    [Fact(DisplayName = "ImportAsync creates a new endpoint mapping with permissions")]
    public async Task ImportAsync_CreatesNewMapping_WithPermissions()
    {
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:read"), "Read");
        _context.Permissions.Add(perm);
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "endpointName": "Test.Get",
                    "description": "Gets a thing",
                    "pathTemplate": "/things/{id}",
                    "httpMethod": "GET",
                    "permissionKeys": ["perm:read"]
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);

        using var verifyCtx = _contextFactory.CreateDbContext();
        var mapping = await verifyCtx.EndpointMappings
            .Include(e => e.Permissions)
            .FirstOrDefaultAsync(e => e.EndpointName == "Test.Get");
        Assert.NotNull(mapping);
        Assert.Equal("/things/{id}", mapping.PathTemplate);
        Assert.Equal("GET", mapping.HttpMethod);
        Assert.Single(mapping.Permissions);
        Assert.Contains(mapping.Permissions, p => p.Key.Value == "perm:read");
    }

    [Fact(DisplayName = "ImportAsync skips existing mapping when OverwriteExisting is false")]
    public async Task ImportAsync_SkipsExistingMapping_WhenOverwriteExistingIsFalse()
    {
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.Skip",
            PathTemplate = "/original/{id}",
            HttpMethod = "GET"
        });
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "endpointName": "Test.Skip",
                    "pathTemplate": "/updated/{id}",
                    "httpMethod": "POST"
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);

        using var verifyCtx = _contextFactory.CreateDbContext();
        var mapping = await verifyCtx.EndpointMappings.FirstOrDefaultAsync(e => e.EndpointName == "Test.Skip");
        Assert.NotNull(mapping);
        Assert.Equal("/original/{id}", mapping.PathTemplate);
        Assert.Equal("GET", mapping.HttpMethod);
    }

    [Fact(DisplayName = "ImportAsync updates existing mapping when OverwriteExisting is true")]
    public async Task ImportAsync_UpdatesExistingMapping_WhenOverwriteExistingIsTrue()
    {
        var perm = new Forge.Core.Features.Permission.Persistence.Permission(PermissionKey.Parse("perm:write"), "Write");
        _context.Permissions.Add(perm);
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.Update",
            PathTemplate = "/original/{id}",
            HttpMethod = "GET"
        });
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                {
                    "endpointName": "Test.Update",
                    "description": "Updated desc",
                    "pathTemplate": "/updated/{id}",
                    "httpMethod": "POST",
                    "permissionKeys": ["perm:write"]
                }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: true);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.UpdatedCount);

        using var verifyCtx = _contextFactory.CreateDbContext();
        var mapping = await verifyCtx.EndpointMappings
            .Include(e => e.Permissions)
            .FirstOrDefaultAsync(e => e.EndpointName == "Test.Update");
        Assert.NotNull(mapping);
        Assert.Equal("Updated desc", mapping.Description);
        Assert.Equal("/updated/{id}", mapping.PathTemplate);
        Assert.Equal("POST", mapping.HttpMethod);
        Assert.Single(mapping.Permissions);
        Assert.Contains(mapping.Permissions, p => p.Key.Value == "perm:write");
    }

    [Fact(DisplayName = "ImportAsync returns error for missing items array")]
    public async Task ImportAsync_ReturnsError_WhenItemsArrayMissing()
    {
        var json = """{ "notItems": [] }""";
        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors!, e => e.Contains("items"));
    }

    [Fact(DisplayName = "ImportAsync(EndpointMappingImportDto) writes valid rows while reporting errors for invalid ones (partial-failure shape)")]
    public async Task ImportAsync_Dto_WritesValidRows_WhileReportingErrors_ForPartialFailure()
    {
        // The JsonElement hand-parser filters out items missing endpointName/pathTemplate/httpMethod
        // before they ever reach per-item validation, so the "Invalid endpoint name" etc. error
        // paths are unreachable from JSON. Driving the typed overload directly is the only way to
        // pin the partial-failure shape: the per-item catch records an error and continues, and the
        // single SaveChangesAsync then commits the items that did survive — which is why ADR-0084 §2
        // forbids gating invalidation on ImportResult.Success.
        var dto = new EndpointMappingImportDto(
            Count: 2,
            Items:
            [
                new EndpointMappingItemDto("Test.Valid", null, "/valid/{id}", "GET", null, null),
                new EndpointMappingItemDto(null!, null, "/invalid/{id}", "GET", null, null)
            ]);

        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await ImportTypedAsync(dto, options);

        Assert.False(result.Success);
        Assert.Equal(1, result.CreatedCount);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors!, e => e.Contains("Invalid endpoint name"));

        using var verifyCtx = _contextFactory.CreateDbContext();
        var mapping = await verifyCtx.EndpointMappings.FirstOrDefaultAsync(e => e.EndpointName == "Test.Valid");
        Assert.NotNull(mapping);
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the endpoint-permission snapshot after a successful import")]
    public async Task ImportAsync_InvalidatesSnapshot_AfterSuccessfulImport()
    {
        var json = """
        {
            "items": [
                { "endpointName": "Test.Get", "pathTemplate": "/things/{id}", "httpMethod": "GET" }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.True(result.Success, $"Import failed: {string.Join(';', result.Errors ?? [])}");
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, _permissionService.InvalidateCallCount);
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the snapshot even when the import reported errors")]
    public async Task ImportAsync_InvalidatesSnapshot_WhenImportReportedErrors()
    {
        var json = """{ "notItems": [] }""";
        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(1, _permissionService.InvalidateCallCount);
    }

    [Fact(DisplayName = "#210: ImportAsync invalidates the snapshot even when the import wrote nothing")]
    public async Task ImportAsync_InvalidatesSnapshot_WhenImportWroteNothing()
    {
        _context.EndpointMappings.Add(new Forge.Core.Features.EndpointMap.Persistence.EndpointMapping
        {
            EndpointName = "Test.Skip",
            PathTemplate = "/original/{id}",
            HttpMethod = "GET"
        });
        await _context.SaveChangesAsync();

        var json = """
        {
            "items": [
                { "endpointName": "Test.Skip", "pathTemplate": "/updated/{id}", "httpMethod": "POST" }
            ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var options = new ImportOptions(FailFast: false, OverwriteExisting: false);

        var result = await _importExport.ImportAsync(document.RootElement, options, CancellationToken.None);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, _permissionService.InvalidateCallCount);
    }

    public void Dispose()
    {
        _context?.Dispose();
    }

    private sealed class RecordingEndpointPermissionService : IEndpointPermissionService
    {
        public int InvalidateCallCount { get; private set; }

        public Task InvalidateSnapshotAsync(CancellationToken cancellationToken)
        {
            InvalidateCallCount++;

            return Task.CompletedTask;
        }

        public Task<Result<IList<string>, Error>> GetEndpointPermissionsAsync(string endpointName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IList<EndpointPermissionMap>> GetEndpointMappingsAsync(string endpointName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> SetEndpointPermissionAsync(EndpointPermissionMap endpointPermission, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<EndpointPermissionMatch?, Error>> GetEndpointPermissionsByPathAsync(string path, string httpMethod, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<EndpointPermissionMap, Error>> CreateEndpointMappingAsync(string endpointName, string pathTemplate, string httpMethod, string? description, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<Error>> DeleteEndpointMappingAsync(string endpointName, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<EndpointPermissionMap, Error>> GetEndpointMappingByNameAsync(string endpointName, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class TestDbContextFactory : IDbContextFactory<ForgeDbContext>
    {
        private readonly DbContextOptions<ForgeDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ForgeDbContext> options)
        {
            _options = options;
        }

        public ForgeDbContext CreateDbContext() => new(_options);

        public Task<ForgeDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ForgeDbContext(_options));
    }
}
