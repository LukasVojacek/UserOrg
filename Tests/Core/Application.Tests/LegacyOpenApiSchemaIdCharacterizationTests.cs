using System.Text.Json;

namespace UserOrganizationManagement.Application.Tests;

// Characterization tests (regression safety net) for the schema ids/shapes produced TODAY by the
// legacy OpenAPI documents (API_user.json / API_administration.json), generated via
// UserOrganizationManagement.API.Composition.HostInfrastructureExtensions.ConfigureSharedOpenApi.
//
// These pin the current output so an upcoming change to ConfigureSharedOpenApi (adding
// OpenApiOptions.CreateSchemaReferenceId) cannot silently rename/collapse legacy or top-level
// value-object schema ids without turning this suite red. Do not "fix" this file to match a future
// desired shape — it exists to lock in what the unchanged code does today.
public sealed class LegacyOpenApiSchemaIdCharacterizationTests
{
    // A representative allow-list (not a full snapshot) of schema ids that must remain present,
    // under their current names, in both legacy documents.
    private static readonly string[] _expectedSchemaIds =
    [
        "ProjectModel",
        "ProjectCreate",
        "ProjectUpdate",
        "ProjectInfoModel",
        "ProjectId",
        "ProjectShortName",
        "UserInfoModel",
        "UserModel",
        "RequestModel",
        "ProviderModel",
        "LocationModel",
        "ProblemDetails",
        "HttpValidationProblemDetails"
    ];

    [Theory]
    [InlineData("Error")]
    [InlineData("DataStagingDataModel")]
    [InlineData("DataStagingPart")]
    public void Should_KeepTheLegacySchemaId_When_TheServiceDocumentDescribesAMovedOperationsSchema(string expectedId)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile("API_service.json")));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(
            schemas.TryGetProperty(expectedId, out _),
            $"Expected schema id '{expectedId}' not found in API_service.json's components.schemas: the moved operations keep their legacy schema ids, and the v2-only CreateSchemaReferenceId delegate (ADR-0062) must not rename nested legacy DTOs.");
    }

    [Theory]
    [InlineData("API_user.json")]
    [InlineData("API_administration.json")]
    public void Should_ContainExpectedSchemaIds_When_GeneratingLegacyDocument(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile(fileName)));

        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        foreach (var expectedId in _expectedSchemaIds)
        {
            Assert.True(
                schemas.TryGetProperty(expectedId, out _),
                $"Expected schema id '{expectedId}' not found in {fileName}'s components.schemas.");
        }
    }

    [Theory]
    [InlineData("API_user.json")]
    [InlineData("API_administration.json")]
    public void Should_KeepProjectIdSchemaEmpty_When_GeneratingLegacyDocument(string fileName)
    {
        // Today, the top-level ProjectId value-object schema serializes as an empty object (no
        // Type/Format survive onto the *component* schema, even though ConfigureSharedOpenApi's
        // schema transformer sets Type=String/Format=uuid on ProjectId occurrences). This looks odd,
        // but it is the current, unmodified behaviour being pinned — any change to it should be
        // deliberate and visible, not a silent side effect of touching CreateSchemaReferenceId.
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile(fileName)));

        var projectId = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProjectId");

        Assert.Equal(JsonValueKind.Object, projectId.ValueKind);
        Assert.Empty(projectId.EnumerateObject());
    }

    [Theory]
    [InlineData("API_user.json")]
    [InlineData("API_administration.json")]
    public void Should_KeepProjectShortNameSchemaEmpty_When_GeneratingLegacyDocument(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile(fileName)));

        var projectShortName = doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("ProjectShortName");

        Assert.Equal(JsonValueKind.Object, projectShortName.ValueKind);
        Assert.Empty(projectShortName.EnumerateObject());
    }

    [Fact]
    public void Should_ExposeSameSchemaIdSet_When_ComparingUserAndAdministrationDocuments()
    {
        // Both legacy documents share ConfigureSharedOpenApi and, today, expose the exact same set
        // of component-schema ids (the path-substring ShouldInclude filter narrows operations, not
        // the referenced schema set). Pin the equality so a future change that starts diverging the
        // two documents' schemas is a visible, deliberate decision.
        using var userDoc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile("API_user.json")));
        using var adminDoc = JsonDocument.Parse(File.ReadAllText(RepoRoot.FindFile("API_administration.json")));

        var userSchemaIds = userDoc.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var adminSchemaIds = adminDoc.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(userSchemaIds, adminSchemaIds);
    }
}
