using System.Reflection;
using System.Text.RegularExpressions;

namespace Core.UnitTests.Shared;

public sealed partial class AuditActionsCatalogTests
{
    public static TheoryData<string> CoreCatalog() =>
    [
        "project.user_role.changed",
        "resource_request.action.settled",
        "project.user.assigned",
        "project.user.unassigned",
        "project.self.created",
        "project.self.updated",
        "project.self.deleted",
        "project.tag.created",
        "project.tag.assigned",
        "project.tag.unassigned",
        "project_tag.self.deleted",
        "project.resource.assigned",
        "project.resource.unassigned",
        "user.provider_admin.granted",
        "user.provider_admin.revoked",
        "user.preferences.updated",
        "user.self.provisioned",
        "onboarding_request.self.accepted",
        "onboarding_request.self.rejected",
        "request.self.created",
        "request.self.accepted",
        "request.self.rejected",
        "request.action_link.settled",
        "request.action_link.viewed",
        "resource_request.self.created",
        "resource_request.self.cancelled",
        "resource_request.approval_link.reissued",
        "resource_request.action.viewed",
        "provider.self.created",
        "provider.self.updated",
        "provider.self.deactivated",
        "provider.self.reactivated",
        "provider.location.added",
        "provider.location.updated",
        "provider.location.removed",
        "provider.resource_assignment.created",
        "provider.resource_assignment.updated",
        "provider.resource_assignment.removed",
        "resource.creation.requested",
        "resource.self.updated",
        "resource.self.deleted",
        "resource.self.published",
        "cache.output.flushed",
    ];

    [GeneratedRegex("^[a-z]+(_[a-z]+)*\\.[a-z]+(_[a-z]+)*\\.[a-z]+(_[a-z]+)*$")]
    private static partial Regex ThreeSegmentAction();

    private static IReadOnlyList<string> ConstantsOf(Type type) =>
        [.. type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f is { IsLiteral: true, FieldType.Name: nameof(String) })
            .Select(f => (string)f.GetRawConstantValue()!)];

    [Theory]
    [MemberData(nameof(CoreCatalog))]
    public void Should_DeclareTheAction_When_ItIsInTheCoreCatalog(string action) =>
        Assert.Contains(action, ConstantsOf(typeof(AuditActions)));

    [Fact]
    public void Should_FollowTheThreeSegmentRule_When_DeclaringAnyAction() =>
        Assert.All(ConstantsOf(typeof(AuditActions)), action => Assert.Matches(ThreeSegmentAction(), action));

    [Fact]
    public void Should_DeclareEveryActionOnce_When_ListingTheCatalog()
    {
        var actions = ConstantsOf(typeof(AuditActions));

        Assert.Equal(actions.Count, actions.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("project.caller_out_of_scope")]
    [InlineData("project.not_found")]
    [InlineData("resource_request.not_awaiting_decision")]
    public void Should_DeclareTheDenialReason_When_NoOtherConstantCarriesIt(string reason) =>
        Assert.Contains(reason, ConstantsOf(typeof(AuditActions.DenialReasons)));

    [Fact]
    public void Should_NestATargetTypesCatalog_When_DeclaringTheActions()
    {
        var targetTypes = typeof(AuditActions).GetNestedType("TargetTypes", BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(targetTypes);
        Assert.Contains("user", ConstantsOf(targetTypes));
    }
}
