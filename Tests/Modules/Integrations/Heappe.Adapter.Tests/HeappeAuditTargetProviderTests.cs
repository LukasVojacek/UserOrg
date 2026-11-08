using Core.DataAccess.Queries.Contracts;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Userorg.Adapter.Heappe.Userorg;

namespace Heappe.Adapter.Tests;

/// <summary>
/// Verifies <see cref="HeappeAuditTargetProvider"/> maps modular read rows to audit-target entries: the stored
/// HEAppE identifier routes the audit (a malformed one skips the row instead of aborting the whole audit), the
/// stored HEAppE URL only labels it (a missing or malformed one leaves <c>HeappeUri</c> null).
/// </summary>
public sealed class HeappeAuditTargetProviderTests
{
    private readonly Mock<IHeappeAuditTargetQuery> _query = new(MockBehavior.Strict);

    private HeappeAuditTargetProvider CreateSut() =>
        new(_query.Object, NullLogger<HeappeAuditTargetProvider>.Instance);

    private void SetupRows(params HeappeAuditTargetRow[] rows) =>
        _query.Setup(q => q.GetAuditTargetsAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(rows.ToList());

    private static HeappeAuditTargetRow Row(string resourceName, string? heappeUrl, string heappeIdentifier = "heappe-1") =>
        new(resourceName, "projA", heappeUrl, heappeIdentifier,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Should_Map_Valid_Row_To_Entry_With_Normalized_Uri()
    {
        SetupRows(Row("RES-1", "https://heappe.example.org"));

        var entries = await CreateSut().GetAuditTargetsAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("RES-1", entry.ResourceName);
        Assert.Equal("heappe-1", entry.HeappeIdentifier.Value);
        Assert.Equal("https://heappe.example.org/", entry.HeappeUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData("not a valid url")]
    [InlineData(null)]
    public async Task Should_Keep_Row_Without_A_Usable_Url_Routed_By_Its_Identifier(string? storedAddress)
    {
        SetupRows(Row("RES-NO-URL", storedAddress, "heappe-2"));

        var entries = await CreateSut().GetAuditTargetsAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("RES-NO-URL", entry.ResourceName);
        Assert.Equal("heappe-2", entry.HeappeIdentifier.Value);
        Assert.Null(entry.HeappeUri);
    }

    [Fact]
    public async Task Should_Skip_Row_With_Invalid_Identifier_And_Keep_Valid_Ones()
    {
        SetupRows(
            Row("RES-1", "https://heappe.example.org"),
            Row("RES-BAD", "https://heappe.example.org", "not a valid identifier!"));

        var entries = await CreateSut().GetAuditTargetsAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(entries);
        Assert.Equal("RES-1", entry.ResourceName);
    }
}
