using System.Net;

using Heappe.Contracts;
using Heappe.Features.Nexus;

using Xunit;

namespace Heappe.UnitTests.Features.Nexus;

/// <summary>
/// One row per failure cause: the HTTP status plus the RFC 9457 <c>type</c> NexusApi stamps on its own answers
/// decide the URL-free cause code (ADR 0074). A HEAppE answer relayed through the proxy carries no Nexus type and
/// keeps today's status mapping.
/// </summary>
public sealed class NexusProblemMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "urn:config-registry:heappe-instance-not-found", HeappeFailureCause.HeappeInstanceNotFound)]
    [InlineData(HttpStatusCode.BadGateway, "urn:config-registry:heappe-upstream-unavailable", HeappeFailureCause.HeappeUpstreamUnavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "urn:config-registry:registry-unavailable", HeappeFailureCause.NexusUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized, null, HeappeFailureCause.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, null, HeappeFailureCause.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, null, HeappeFailureCause.UpstreamError)]
    [InlineData(HttpStatusCode.NotFound, null, HeappeFailureCause.UpstreamError)]
    [InlineData(HttpStatusCode.BadGateway, null, HeappeFailureCause.UpstreamError)]
    [InlineData(HttpStatusCode.BadGateway, "about:blank", HeappeFailureCause.UpstreamError)]
    public void CauseFor_maps_status_and_problem_type_to_the_cause(HttpStatusCode status, string? problemType, string expectedCause)
    {
        Assert.Equal(expectedCause, NexusProblemMapper.CauseFor(status, problemType));
    }

    [Fact]
    public void ProblemTypeOf_reads_the_type_of_a_problem_json_body()
    {
        var type = NexusProblemMapper.ProblemTypeOf(
            "application/problem+json",
            """{"type":"urn:config-registry:heappe-upstream-unavailable","title":"HEAppE instance unavailable","status":502}""");

        Assert.Equal("urn:config-registry:heappe-upstream-unavailable", type);
    }

    [Theory]
    [InlineData("application/json", """{"type":"urn:config-registry:heappe-upstream-unavailable"}""")]
    [InlineData(null, """{"type":"urn:config-registry:heappe-upstream-unavailable"}""")]
    [InlineData("application/problem+json", "not json")]
    [InlineData("application/problem+json", """{"title":"no type"}""")]
    public void ProblemTypeOf_returns_null_when_the_body_is_not_a_typed_problem(string? mediaType, string body)
    {
        Assert.Null(NexusProblemMapper.ProblemTypeOf(mediaType, body));
    }
}
