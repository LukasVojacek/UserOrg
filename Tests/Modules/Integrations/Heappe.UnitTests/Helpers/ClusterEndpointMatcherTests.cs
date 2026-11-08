using Heappe.Helpers;

using Xunit;

namespace Heappe.UnitTests.Helpers;

public class ClusterEndpointMatcherTests
{
    [Theory]
    // The bug (#218): Waldur stores the login endpoint as host:port, HEAppE keeps the host alone.
    [InlineData("login.efp.discoverer.bg", "login.efp.discoverer.bg:2224", true)]
    // A scheme already on the endpoint is honoured, host still wins.
    [InlineData("login.efp.discoverer.bg", "https://login.efp.discoverer.bg:2224", true)]
    // The pre-existing default-port case keeps matching.
    [InlineData("login.barbora", "login.barbora", true)]
    // Different hosts must not match, port notwithstanding.
    [InlineData("login.barbora", "login.karolina:22", false)]
    // Legacy-permissive: the master node only has to CONTAIN the (port-stripped) login host, not equal
    // it. Kept deliberately because the substring match is long-standing and may be relied upon.
    [InlineData("login.barbora.backup", "login.barbora:2224", true)]
    public void MatchesMasterNode_ComparesHostIgnoringSchemeAndPort(string masterNodeName, string endpoint, bool expected)
    {
        Assert.Equal(expected, ClusterEndpointMatcher.MatchesMasterNode(masterNodeName, endpoint));
    }

    [Fact]
    public void MatchesMasterNode_NullEndpoint_ReturnsFalseWithoutThrowing()
    {
        // Regression guard: the partition-sync path can pass a null login endpoint (the DB column is
        // nullable). The previous MasterNodeName.Equals(null) returned false; the matcher must too.
        Assert.False(ClusterEndpointMatcher.MatchesMasterNode("login.barbora", null!));
    }

    [Fact]
    public void MatchesMasterNode_UnparseableEndpoint_FallsBackToSubstringWithoutThrowing()
    {
        // A space makes "ssh://bad host" an invalid URI; the matcher must not throw and must
        // degrade to the legacy case-insensitive Contains() behavior.
        Assert.True(ClusterEndpointMatcher.MatchesMasterNode("node-BAD HOST-01", "bad host"));
        Assert.False(ClusterEndpointMatcher.MatchesMasterNode("login.barbora", "bad host"));
    }
}
