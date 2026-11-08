using System.Reflection;

using Lukdrasil.StepUpLogging;

namespace Core.UnitTests.Audit;

public sealed class AuditCoverageTests
{
    private const BindingFlags HandlerFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static TheoryData<Type> P1Endpoints =>
    [
        typeof(Core.Features.Project.ChangeProjectUserRole.ChangeProjectUserRoleEndpoint),
        typeof(Core.Features.Project.AssignProjectUser.AssignProjectUserEndpoint),
        typeof(Core.Features.Project.UnassignProjectUser.UnassignProjectUserEndpoint),
        typeof(Core.Features.User.PromoteToProviderAdmin.PromoteToProviderAdminEndpoint),
        typeof(Core.Features.User.ResetToPlatformUser.ResetProviderAdminEndpoint),
        typeof(Core.Features.Project.DeleteProject.DeleteProjectEndpoint),
        typeof(Core.Features.OnboardingRequest.AcceptOnboardingRequest.AcceptOnboardingRequestEndpoint),
        typeof(Core.Features.OnboardingRequest.RejectOnboardingRequest.RejectOnboardingRequestEndpoint),
        typeof(Core.Features.Request.AcceptRequest.AcceptRequestEndpoint),
        typeof(Core.Features.Request.RejectRequest.RejectRequestEndpoint),
        typeof(Core.Features.ResourceRequest.HandleResourceRequestAction.HandleResourceRequestActionEndpoint),
        typeof(Core.Features.ResourceRequest.GetResourceRequestAction.GetResourceRequestActionEndpoint),
        typeof(Core.Features.Request.HandleActionLink.HandleActionLinkEndpoint),
        typeof(Core.Features.Request.GetPublicRequestInfo.GetPublicRequestInfoEndpoint),
        typeof(Core.Features.ResourceRequest.ResendApprovalEmail.ResendApprovalEmailEndpoint),
    ];

    public static TheoryData<Type> P2Endpoints =>
    [
        typeof(Core.Features.Provider.CreateProvider.CreateProviderEndpoint),
        typeof(Core.Features.Provider.UpdateProvider.UpdateProviderEndpoint),
        typeof(Core.Features.Provider.DeactivateProvider.DeactivateProviderEndpoint),
        typeof(Core.Features.Provider.ReactivateProvider.ReactivateProviderEndpoint),
        typeof(Core.Features.Provider.AddLocation.AddLocationEndpoint),
        typeof(Core.Features.Provider.UpdateLocation.UpdateLocationEndpoint),
        typeof(Core.Features.Provider.RemoveLocation.RemoveLocationEndpoint),
        typeof(Core.Features.Provider.ResourceAssignment.AssignResourceEndpoint),
        typeof(Core.Features.Provider.ResourceAssignment.UpdateAssignmentEndpoint),
        typeof(Core.Features.Provider.ResourceAssignment.RemoveAssignmentEndpoint),
        typeof(Core.Features.Resource.CreateResourceOnLocation.CreateResourceOnLocationEndpoint),
        typeof(Core.Features.Resource.UpdateResource.UpdateResourceEndpoint),
        typeof(Core.Features.Resource.DeleteResource.DeleteResourceEndpoint),
        typeof(Core.Features.Resource.PublishResource.PublishResourceEndpoint),
        typeof(Core.Features.Project.CreateProject.CreateProjectEndpoint),
        typeof(Core.Features.Project.UpdateProject.UpdateProjectEndpoint),
        typeof(Core.Features.Project.CreateProjectTag.CreateProjectTagEndpoint),
        typeof(Core.Features.Project.AssignProjectTag.AssignProjectTagEndpoint),
        typeof(Core.Features.Project.UnassignProjectTag.UnassignProjectTagEndpoint),
        typeof(Core.Features.Project.AssignProjectResource.AssignProjectResourceEndpoint),
        typeof(Core.Features.Project.UnassignProjectResource.UnassignProjectResourceEndpoint),
        typeof(Core.Features.ProjectTag.DeleteProjectTag.DeleteProjectTagEndpoint),
        typeof(Core.Features.ResourceRequest.CreateResourceRequest.CreateResourceRequestEndpoint),
        typeof(Core.Features.ResourceRequest.CancelResourceRequest.CancelResourceRequestEndpoint),
        typeof(Core.Features.Request.CreateRequest.CreateRequestEndpoint),
        typeof(Core.Features.User.UpdateUserPreferences.UpdateUserPreferencesEndpoint),
        typeof(Core.Features.Cache.ResetAllCaches.ResetAllCachesEndpoint),
    ];

    [Theory]
    [MemberData(nameof(P1Endpoints))]
    [MemberData(nameof(P2Endpoints))]
    public void Should_TakeAnAuditLogger_When_TheEndpointIsInTheAuditedCatalog(Type endpoint) =>
        AssertTakesAnAuditLogger(endpoint, "HandleAsync");

    [Fact]
    public void Should_TakeAnAuditLogger_When_TheCallerIsProvisioned() =>
        AssertTakesAnAuditLogger(
            typeof(Core.Features.UserInfo.UseCases.CallerProvisioning),
            nameof(Core.Features.UserInfo.UseCases.CallerProvisioning.ProvisionAsync));

    private static void AssertTakesAnAuditLogger(Type type, string handlerName)
    {
        ArgumentNullException.ThrowIfNull(type);
        var handler = type.GetMethod(handlerName, HandlerFlags);

        Assert.NotNull(handler);
        Assert.Contains(handler.GetParameters(), parameter =>
            parameter.ParameterType.IsGenericType
            && parameter.ParameterType.GetGenericTypeDefinition() == typeof(IAuditLogger<>));
    }
}
