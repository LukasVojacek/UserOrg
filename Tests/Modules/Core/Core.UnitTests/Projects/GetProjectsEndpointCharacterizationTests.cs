using Core.DataAccess.Queries.Contracts;
using Core.Domain.Shared;
using Core.Features.Project.GetProjects;
using Core.Shared.Paging;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using Moq;

namespace Core.UnitTests.Projects;

/// <summary>
/// Characterization tests for <see cref="GetProjectsEndpoint.HandleAsync"/> covering the valid short-name and
/// contact-email path that <see cref="GetProjectsEndpointTests"/> leaves out: each parsed value reaches the
/// <see cref="ProjectFilter"/>, with the requested page, and the page is answered 200.
/// </summary>
public sealed class GetProjectsEndpointCharacterizationTests
{
    [Fact]
    public async Task Valid_short_name_and_contact_email_reach_the_filter_with_the_requested_page()
    {
        var shortName = ProjectShortName.Create("PROJ-1");
        var caller = new Caller(
            new IdpId("u"), "Owner", null,
            new Dictionary<string, IReadOnlySet<ProjectShortName>> { ["Owner"] = new HashSet<ProjectShortName> { shortName } });
        var query = new Mock<IProjectQuery>();
        query.Setup(q => q.GetProjectsAsync(It.IsAny<ProjectFilter>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryPage<ProjectSummary>([], 0));

        var result = await GetProjectsEndpoint.HandleAsync(
            new DefaultHttpContext(),
            new GetProjectsEndpoint.Request("PROJ-1", "owner@example.org", null, null, null),
            caller,
            new PageRequest(2, 10),
            query.Object,
            Options.Create(new WellKnownRoles()),
            Options.Create(new PaginationOptions()),
            CancellationToken.None);

        Assert.IsType<Ok<IReadOnlyList<GetProjectsEndpoint.Response>>>(result);
        query.Verify(q => q.GetProjectsAsync(
            It.Is<ProjectFilter>(f =>
                f.ShortName == shortName
                && f.ContactEmail == Email.Create("owner@example.org")
                && f.DomainTypeName == null
                && f.StartDate == null
                && f.EndDate == null
                && f.ScopeShortNames!.SequenceEqual(new[] { shortName })
                && f.ProviderName == null),
            2,
            10,
            It.IsAny<CancellationToken>()));
    }
}
