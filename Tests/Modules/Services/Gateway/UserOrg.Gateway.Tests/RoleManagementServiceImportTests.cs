using System.Text;
using System.Text.Json;

using Forge.Core.Features.EndpointMap.Services;
using Forge.Core.Features.ImportExport;
using Forge.Core.Features.Permission.Services;
using Forge.Core.Features.Role.Services;

using Lukdrasil.Error;
using Lukdrasil.Result;

using UserOrg.Gateway.Features.RoleManagement;
using UserOrg.Gateway.Features.RoleManagement.Services;

namespace UserOrg.Gateway.Tests;

/// <summary>
/// Characterizes <see cref="RoleManagementService.ImportRolesAsync"/>: the three import features must all
/// be registered, the envelope needs a string <c>formatVersion</c> and a <c>features</c> object, the
/// sections present are imported permissions first, then endpoint mappings, then roles, and their
/// counts and errors are summed into one summary. Bad JSON and a throwing feature become failures.
/// </summary>
public sealed class RoleManagementServiceImportTests
{
    private const string FullEnvelope = """
        {
          "formatVersion": "1.0",
          "features": {
            "roles": [{"name": "r"}],
            "permissions": [{"name": "p"}],
            "endpointMappings": [{"name": "e"}]
          }
        }
        """;

    private readonly List<string> _calls = [];

    [Theory]
    [InlineData("roles", "RolesFeatureNotFound", "Roles import/export feature is not registered")]
    [InlineData("permissions", "PermissionsFeatureNotFound", "Permissions import/export feature is not registered")]
    [InlineData("endpointMappings", "EndpointMappingsFeatureNotFound", "EndpointMappings import/export feature is not registered")]
    public async Task Should_FailNamingTheFeature_When_AnImportFeatureIsNotRegistered(string missing, string expectedCode, string expectedDescription)
    {
        var features = AllFeatures().Where(feature => feature.FeatureName != missing).ToArray();

        var result = await RunImportAsync(FullEnvelope, features);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedDescription, result.Error.Description);
        Assert.Empty(_calls);
    }

    [Fact]
    public async Task Should_FindTheFeaturesIgnoringCase_When_TheirNamesDifferInCase()
    {
        var features = new[] { Feature("ROLES"), Feature("Permissions"), Feature("ENDPOINTMAPPINGS") };

        var result = await RunImportAsync(FullEnvelope, features);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, _calls.Count);
    }

    [Theory]
    [InlineData("""{"features":{}}""")]
    [InlineData("""{"formatVersion":1,"features":{}}""")]
    public async Task Should_FailAsInvalidFormat_When_TheFormatVersionIsMissingOrNotAString(string envelope)
    {
        var result = await RunImportAsync(envelope, AllFeatures());

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidFormat", result.Error.Code);
        Assert.Equal("Missing or invalid 'formatVersion' property", result.Error.Description);
    }

    [Theory]
    [InlineData("""{"formatVersion":"1.0"}""")]
    [InlineData("""{"formatVersion":"1.0","features":[]}""")]
    public async Task Should_FailAsInvalidFormat_When_TheFeaturesAreMissingOrNotAnObject(string envelope)
    {
        var result = await RunImportAsync(envelope, AllFeatures());

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidFormat", result.Error.Code);
        Assert.Equal("Missing or invalid 'features' object", result.Error.Description);
    }

    [Fact]
    public async Task Should_FailAsInvalidJson_When_TheStreamIsNotJson()
    {
        var result = await RunImportAsync("{ not json", AllFeatures());

        Assert.False(result.IsSuccess);
        Assert.Equal("InvalidJson", result.Error.Code);
        Assert.StartsWith("Invalid JSON: ", result.Error.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Should_AcceptTrailingCommas_When_TheEnvelopeHasThem()
    {
        var result = await RunImportAsync("""{"formatVersion":"1.0","features":{"roles":[],},}""", AllFeatures());

        Assert.True(result.IsSuccess);
        Assert.Equal(["roles"], _calls);
    }

    [Fact]
    public async Task Should_FailAsImportFailed_When_AFeatureThrows()
    {
        var features = new[] { Feature("roles"), Feature("permissions", failure: new InvalidOperationException("db down")), Feature("endpointMappings") };

        var result = await RunImportAsync(FullEnvelope, features);

        Assert.False(result.IsSuccess);
        Assert.Equal("ImportFailed", result.Error.Code);
        Assert.Equal("Failed to import configuration: db down", result.Error.Description);
    }

    [Fact]
    public async Task Should_ImportPermissionsThenEndpointMappingsThenRoles_When_AllSectionsArePresent()
    {
        var result = await RunImportAsync(FullEnvelope, AllFeatures());

        Assert.True(result.IsSuccess);
        Assert.Equal(["permissions", "endpointMappings", "roles"], _calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Should_PassTheOverwriteFlagWithoutFailFast_When_ImportingEachSection(bool overwriteExisting)
    {
        var features = AllFeatures();

        await RunImportAsync(FullEnvelope, features, overwriteExisting);

        Assert.All(features, feature => Assert.Equal(new ImportOptions(FailFast: false, OverwriteExisting: overwriteExisting), feature.ReceivedOptions));
    }

    [Fact]
    public async Task Should_HandEachFeatureItsOwnSection_When_ImportingTheEnvelope()
    {
        var features = AllFeatures();

        await RunImportAsync(FullEnvelope, features);

        Assert.Equal("""[{"name": "r"}]""", features[0].ReceivedPayload);
        Assert.Equal("""[{"name": "p"}]""", features[1].ReceivedPayload);
        Assert.Equal("""[{"name": "e"}]""", features[2].ReceivedPayload);
    }

    [Fact]
    public async Task Should_ImportOnlyThePresentSections_When_SomeSectionsAreAbsent()
    {
        var result = await RunImportAsync("""{"formatVersion":"1.0","features":{"endpointMappings":[]}}""", AllFeatures());

        Assert.True(result.IsSuccess);
        Assert.Equal(["endpointMappings"], _calls);
        Assert.Equal(new ImportExportSummaryShape(true, 0, 0, 0, null), Shape(result.Value));
    }

    [Fact]
    public async Task Should_SumTheCountsAndReportSuccess_When_NoSectionReportsErrors()
    {
        var features = new[]
        {
            Feature("roles", new ImportResult(true, CreatedCount: 1, UpdatedCount: 10)),
            Feature("permissions", new ImportResult(true, CreatedCount: 2, UpdatedCount: 20, Errors: [])),
            Feature("endpointMappings", new ImportResult(true, CreatedCount: 4, UpdatedCount: 40)),
        };

        var result = await RunImportAsync(FullEnvelope, features);

        Assert.True(result.IsSuccess);
        Assert.Equal(new ImportExportSummaryShape(true, 7, 70, 0, null), Shape(result.Value));
    }

    [Fact]
    public async Task Should_CollectEveryErrorInImportOrderAndReportFailure_When_SectionsReportErrors()
    {
        var features = new[]
        {
            Feature("roles", new ImportResult(false, CreatedCount: 1, Errors: ["role-error"])),
            Feature("permissions", new ImportResult(false, Errors: ["perm-error-1", "perm-error-2"])),
            Feature("endpointMappings", new ImportResult(false, UpdatedCount: 3, Errors: ["endpoint-error"])),
        };

        var result = await RunImportAsync(FullEnvelope, features);

        Assert.True(result.IsSuccess);
        var summary = result.Value;
        Assert.False(summary.Success);
        Assert.Equal(1, summary.CreatedCount);
        Assert.Equal(3, summary.UpdatedCount);
        Assert.Equal(4, summary.ErrorCount);
        Assert.Equal(["perm-error-1", "perm-error-2", "endpoint-error", "role-error"], summary.Errors!);
    }

    private static ImportExportSummaryShape Shape(ImportExportSummary summary) =>
        new(summary.Success, summary.CreatedCount, summary.UpdatedCount, summary.ErrorCount, summary.Errors);

    private RecordingFeature[] AllFeatures() => [Feature("roles"), Feature("permissions"), Feature("endpointMappings")];

    private RecordingFeature Feature(string name, ImportResult? result = null, Exception? failure = null) =>
        new(name, result ?? new ImportResult(true), failure, _calls);

    private static async Task<Result<ImportExportSummary, Error>> RunImportAsync(
        string envelope,
        IEnumerable<IForgeImportExport> features,
        bool overwriteExisting = false)
    {
        var service = new RoleManagementService(
            Mock.Of<IRoleService>(),
            Mock.Of<IPermissionService>(),
            Mock.Of<IEndpointPermissionService>(),
            features);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(envelope));
        return await service.ImportRolesAsync(stream, overwriteExisting, TestContext.Current.CancellationToken);
    }

    private sealed record ImportExportSummaryShape(bool Success, int CreatedCount, int UpdatedCount, int ErrorCount, IReadOnlyCollection<string>? Errors);

    private sealed class RecordingFeature(string name, ImportResult result, Exception? failure, List<string> calls) : IForgeImportExport
    {
        public string FeatureName => name;

        public string? ReceivedPayload { get; private set; }

        public ImportOptions? ReceivedOptions { get; private set; }

        public Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ImportResult> ImportAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
        {
            calls.Add(name);
            ReceivedPayload = payload.GetRawText();
            ReceivedOptions = options;
            return failure is null ? Task.FromResult(result) : Task.FromException<ImportResult>(failure);
        }
    }
}
