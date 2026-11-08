using System.Reflection;
using System.Text.Json;

using Forge.Core.Features.ImportExport;

namespace Forge.Core.Tests.Features.ImportExport;

public sealed class TypedForgeImportExportTests
{
    private static readonly ImportOptions DefaultOptions = new();

    private static JsonElement Payload(string json = """{"items":[]}""")
        => JsonDocument.Parse(json).RootElement.Clone();

    [Fact(DisplayName = "The public JsonElement import entry point cannot be overridden by an importer")]
    public void PublicJsonImportEntryPoint_IsNotOverridable()
    {
        var entryPoint = typeof(TypedForgeImportExport<TestPayload>).GetMethod(
            nameof(TypedForgeImportExport<TestPayload>.ImportAsync),
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: [typeof(JsonElement), typeof(ImportOptions), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(entryPoint);
        Assert.True(
            entryPoint.IsFinal || !entryPoint.IsVirtual,
            "ImportAsync(JsonElement, ...) must not be overridable: an importer could bypass cache invalidation.");
    }

    [Fact(DisplayName = "Both import hooks are abstract, so a new importer cannot omit invalidation")]
    public void ImportHooks_AreAbstract()
    {
        var importCore = typeof(TypedForgeImportExport<TestPayload>).GetMethod(
            "ImportCoreAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var invalidate = typeof(TypedForgeImportExport<TestPayload>).GetMethod(
            "InvalidateAffectedCachesAsync", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(importCore);
        Assert.NotNull(invalidate);
        Assert.True(importCore.IsAbstract, "ImportCoreAsync must be abstract.");
        Assert.True(invalidate.IsAbstract, "InvalidateAffectedCachesAsync must be abstract, not virtual.");
    }

    [Fact(DisplayName = "The typed ImportAsync(TPayload) hook is protected, not a public write path that bypasses invalidation")]
    public void TypedImportHook_IsProtectedNotPublic()
    {
        var typedImport = typeof(TypedForgeImportExport<TestPayload>).GetMethod(
            "ImportAsync",
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(TestPayload), typeof(ImportOptions), typeof(CancellationToken)],
            modifiers: null);

        Assert.NotNull(typedImport);
        Assert.True(typedImport.IsFamily, "ImportAsync(TPayload, ...) must be protected: a public overload is an invalidation-free write path.");
        Assert.False(typedImport.IsPublic);
    }

    [Fact(DisplayName = "No generic IForgeImportExport survives: implementing one would satisfy DI while bypassing invalidation")]
    public void GenericImportExportInterface_DoesNotExist()
    {
        var escapeHatches = typeof(IForgeImportExport).Assembly
            .GetTypes()
            .Where(type => type.IsInterface && type.IsGenericTypeDefinition && typeof(IForgeImportExport).IsAssignableFrom(type))
            .ToList();

        Assert.Empty(escapeHatches);
    }

    [Fact(DisplayName = "A successful import invalidates the affected caches, after the import wrote")]
    public async Task Import_Success_InvalidatesCachesAfterImportCoreCompletes()
    {
        var importer = new TestImporter(new ImportResult(true, CreatedCount: 3));

        var result = await importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None);

        Assert.Equal(["core", "invalidate"], importer.CallOrder);
        Assert.True(result.Success);
    }

    [Fact(DisplayName = "A partial import (Updated: 79, Errors: 1) still invalidates the caches")]
    public async Task Import_PartialWriteWithErrors_StillInvalidatesCaches()
    {
        var partial = new ImportResult(false, UpdatedCount: 79, Errors: ["row 12 failed"]);
        var importer = new TestImporter(partial);

        var result = await importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None);

        Assert.Equal(1, importer.InvalidationCount);
        Assert.Equal(79, result.UpdatedCount);
        Assert.False(result.Success);
    }

    [Fact(DisplayName = "Invalidation runs with a non-cancellable token even when the request token is already cancelled")]
    public async Task Import_RequestTokenCancelled_InvalidationStillRunsWithNonCancellableToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var importer = new TestImporter(new ImportResult(true, CreatedCount: 1));

        var result = await importer.ImportAsync(Payload(), DefaultOptions, cts.Token);

        Assert.Equal(1, importer.InvalidationCount);
        Assert.NotNull(importer.ReceivedInvalidationToken);
        Assert.False(importer.ReceivedInvalidationToken!.Value.CanBeCanceled,
            "the DB write already committed, so invalidation must not honour the request's (possibly tripped) token");
        Assert.True(result.Success);
    }

    [Fact(DisplayName = "A throwing ImportCoreAsync still invalidates the caches before the exception propagates")]
    public async Task Import_ImportCoreThrows_StillInvalidatesCaches()
    {
        var importer = new TestImporter(new ImportResult(true), coreFailure: new CoreFailureException("boom"));

        await Assert.ThrowsAsync<CoreFailureException>(
            () => importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None));

        Assert.Equal(1, importer.InvalidationCount);
    }

    [Fact(DisplayName = "When both ImportCoreAsync and invalidation throw, ImportCoreAsync's exception is the one that surfaces")]
    public async Task Import_ImportCoreThrows_AndInvalidationAlsoThrows_OriginalExceptionSurfacesUnmasked()
    {
        var importer = new TestImporter(
            new ImportResult(true),
            invalidationFailure: new InvalidOperationException("cache is gone"),
            coreFailure: new CoreFailureException("boom"));

        var thrown = await Assert.ThrowsAsync<CoreFailureException>(
            () => importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None));

        Assert.Equal("boom", thrown.Message);
        Assert.Equal(1, importer.InvalidationCount);
    }

    [Fact(DisplayName = "A throwing invalidation surfaces as an import error, keeping the counts of the writes that happened")]
    public async Task Import_InvalidationThrows_IsReportedAsImportErrorAndPreservesCounts()
    {
        var importer = new TestImporter(
            new ImportResult(true, CreatedCount: 2, UpdatedCount: 79),
            invalidationFailure: new InvalidOperationException("cache is gone"));

        var result = await importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors!, error => error.Contains("cache is gone", StringComparison.Ordinal));
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(79, result.UpdatedCount);
    }

    [Fact(DisplayName = "A throwing invalidation keeps the errors the import itself reported")]
    public async Task Import_InvalidationThrows_KeepsExistingErrors()
    {
        var importer = new TestImporter(
            new ImportResult(false, UpdatedCount: 79, Errors: ["row 12 failed"]),
            invalidationFailure: new InvalidOperationException("cache is gone"));

        var result = await importer.ImportAsync(Payload(), DefaultOptions, CancellationToken.None);

        Assert.Contains("row 12 failed", result.Errors!);
        Assert.Equal(2, result.Errors!.Count());
    }

    [Fact(DisplayName = "The typed deserialize path remains available to an importer's ImportCoreAsync")]
    public async Task DeserializeAndImport_PassesTypedPayloadToTypedImport()
    {
        var importer = new DeserializingTestImporter();

        var result = await importer.ImportAsync(
            Payload("""{"name":"alpha"}"""), DefaultOptions, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("alpha", importer.ReceivedPayload?.Name);
        Assert.Equal(1, importer.InvalidationCount);
    }

    private sealed record TestPayload
    {
        public string? Name { get; init; }
    }

    private sealed class CoreFailureException(string message) : Exception(message);

    private class TestImporter : TypedForgeImportExport<TestPayload>
    {
        private readonly ImportResult _coreResult;
        private readonly Exception? _invalidationFailure;
        private readonly Exception? _coreFailure;

        public TestImporter(ImportResult coreResult, Exception? invalidationFailure = null, Exception? coreFailure = null)
        {
            _coreResult = coreResult;
            _invalidationFailure = invalidationFailure;
            _coreFailure = coreFailure;
        }

        public int InvalidationCount { get; private set; }

        public List<string> CallOrder { get; } = [];

        public CancellationToken? ReceivedInvalidationToken { get; private set; }

        public override string FeatureName => "test";

        public override Task ExportAsync(Utf8JsonWriter writer, ExportOptions options, CancellationToken cancellationToken)
            => Task.CompletedTask;

        protected override Task<ImportResult> ImportAsync(TestPayload payload, ImportOptions options, CancellationToken cancellationToken)
            => Task.FromResult(_coreResult);

        protected override Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
        {
            CallOrder.Add("core");
            if (_coreFailure is not null)
                throw _coreFailure;

            return Task.FromResult(_coreResult);
        }

        protected override Task InvalidateAffectedCachesAsync(CancellationToken cancellationToken)
        {
            CallOrder.Add("invalidate");
            InvalidationCount++;
            ReceivedInvalidationToken = cancellationToken;
            return _invalidationFailure is null ? Task.CompletedTask : Task.FromException(_invalidationFailure);
        }
    }

    private sealed class DeserializingTestImporter : TestImporter
    {
        public DeserializingTestImporter()
            : base(new ImportResult(true))
        {
        }

        public TestPayload? ReceivedPayload { get; private set; }

        protected override Task<ImportResult> ImportAsync(TestPayload payload, ImportOptions options, CancellationToken cancellationToken)
        {
            ReceivedPayload = payload;
            return Task.FromResult(new ImportResult(true));
        }

        protected override Task<ImportResult> ImportCoreAsync(JsonElement payload, ImportOptions options, CancellationToken cancellationToken)
            => DeserializeAndImportAsync(payload, options, cancellationToken);
    }
}
