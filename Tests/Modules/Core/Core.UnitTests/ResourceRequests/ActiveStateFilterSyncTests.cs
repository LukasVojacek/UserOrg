using Core.DataAccess.ResourceRequests;

namespace Core.UnitTests.ResourceRequests;

/// <summary>
/// Guards that the hand-written SQL filter of the single-active unique index (ADR-0066) names exactly the
/// <see cref="ResourceRequestState.NonTerminal"/> states — so adding/removing a non-terminal state can never
/// silently drift from the index that enforces the invariant.
/// </summary>
public sealed class ActiveStateFilterSyncTests
{
    [Fact]
    public void Should_NameExactlyTheNonTerminalStates_When_FilteredIndexSqlInspected()
    {
        var filter = ProjectResourceRequestConfiguration.ActiveStateFilter;

        Assert.All(ResourceRequestState.NonTerminal, s => Assert.Contains($"N'{s.Value}'", filter));

        var quotedCount = filter.Split("N'").Length - 1;
        Assert.Equal(ResourceRequestState.NonTerminal.Count, quotedCount);
    }
}
