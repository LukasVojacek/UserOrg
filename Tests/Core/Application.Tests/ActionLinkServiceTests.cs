using Microsoft.Extensions.Options;

using Moq;

using UserOrganizationManagement.Application.Abstraction;
using UserOrganizationManagement.Application.UserOrg.IServices;
using UserOrganizationManagement.Application.UserOrg.Services;
using UserOrganizationManagement.Domain.Entities;
using UserOrganizationManagement.Domain.Enums;
using UserOrganizationManagement.Domain.IRepositories;

using ActionLinkConfig = UserOrganizationManagement.Domain.Configuration.ActionLink;

namespace UserOrganizationManagement.Application.Tests;

public sealed class ActionLinkServiceTests
{
    private const string HandledLink = "aabbccdd";

    // Issue #278: the burn used to be flushed by its own save before the request was settled, which
    // split it from the accept it belongs to. It now rides the settle's save - and the trailing save
    // keeps it flushed on both paths that can settle without writing anything: DeclineAssignRequestAsync's
    // early return for an already-settled request, and an accept that matches no branch in
    // AcceptResourceAssignmentAsync (an if / else-if with no else, so the miss is silent).
    [Theory]
    [InlineData(EActionType.Reject)]
    [InlineData(EActionType.Accept)]
    public async Task Handle_SettlingSavesNothing_BurnsLinksAfterSettling(EActionType handledAction)
    {
        var links = BuildLinksForOneRequest(handledAction);
        var calls = new List<string>();
        var uow = BuildUnitOfWork(links, calls);
        var requestService = BuildRequestServiceSavingNothing(calls);

        var handled = await CreateSut(uow).Handle(HandledLink, requestService.Object, CancellationToken.None);

        Assert.True(handled);
        Assert.All(links, link => Assert.True(link.IsDeleted));
        Assert.Equal(["settle", "save"], calls);
    }

    private static IList<ActionLink> BuildLinksForOneRequest(EActionType handledAction)
    {
        var requestId = Guid.NewGuid();
        var siblingAction = handledAction == EActionType.Accept ? EActionType.Reject : EActionType.Accept;
        return
        [
            new ActionLink { Action = handledAction, Link = HandledLink, RequestId = requestId },
            new ActionLink { Action = siblingAction, Link = "eeff0011", RequestId = requestId }
        ];
    }

    private static Mock<IUserOrgUnitOfWork> BuildUnitOfWork(IList<ActionLink> links, List<string> calls)
    {
        var repository = new Mock<IActionLinkRepository>();
        repository.Setup(r => r.GetByLink(HandledLink, It.IsAny<CancellationToken>()))
            .ReturnsAsync(links.Single(l => l.Link == HandledLink));
        repository.Setup(r => r.GetRelatedToRequest(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(links);

        var uow = new Mock<IUserOrgUnitOfWork>();
        uow.Setup(u => u.ActionLinkRepository).Returns(repository.Object);
        uow.Setup(u => u.SaveAsync(It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("save"))
            .ReturnsAsync(1);
        return uow;
    }

    // Mirrors a settle that writes nothing: the request is already accepted or rejected, so neither
    // call reaches a branch that saves.
    private static Mock<IProjectRequestService> BuildRequestServiceSavingNothing(List<string> calls)
    {
        var requestService = new Mock<IProjectRequestService>();
        requestService
            .Setup(s => s.DeclineAssignRequestAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("settle"))
            .Returns(Task.CompletedTask);
        requestService
            .Setup(s => s.AcceptAssignRequestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add("settle"))
            .ReturnsAsync(ERequestState.Accepted);
        return requestService;
    }

    private static ActionLinkService CreateSut(Mock<IUserOrgUnitOfWork> uow) =>
        new(Options.Create(new ActionLinkConfig()), uow.Object, new Mock<IRequestRepository>().Object);
}
