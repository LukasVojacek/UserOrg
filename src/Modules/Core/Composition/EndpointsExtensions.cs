using Core.Features.Cache.ResetAllCaches;
using Core.Features.Country.GetCountries;
using Core.Features.Enums.GetAllocationUnitType;
using Core.Features.Enums.GetAuthenticationTypes;
using Core.Features.Enums.GetLocationTypes;
using Core.Features.Enums.GetOperationModes;
using Core.Features.Enums.GetProjectDomainTypes;
using Core.Features.Enums.GetRequestStates;
using Core.Features.Enums.GetRequestTypes;
using Core.Features.Location.GetLocation;
using Core.Features.Location.GetLocations;
using Core.Features.Location.GetLocationsProviderAdmin;
using Core.Features.Meta.GetVersion;
using Core.Features.OnboardingRequest.AcceptOnboardingRequest;
using Core.Features.OnboardingRequest.CreateOnboardingRequest;
using Core.Features.OnboardingRequest.GetMyOnboardingRequests;
using Core.Features.OnboardingRequest.RejectOnboardingRequest;
using Core.Features.Project.AssignProjectResource;
using Core.Features.Project.AssignProjectTag;
using Core.Features.Project.AssignProjectUser;
using Core.Features.Project.ChangeProjectUserRole;
using Core.Features.Project.CreateProject;
using Core.Features.Project.CreateProjectTag;
using Core.Features.Project.DeleteProject;
using Core.Features.Project.GetExtendedProject;
using Core.Features.Project.GetProject;
using Core.Features.Project.GetProjects;
using Core.Features.Project.GetProjectsProviderAdmin;
using Core.Features.Project.GetProjectTemplates;
using Core.Features.Project.GetProjectUsers;
using Core.Features.Project.GetValidRoles;
using Core.Features.Project.UnassignProjectResource;
using Core.Features.Project.UnassignProjectTag;
using Core.Features.Project.UnassignProjectUser;
using Core.Features.Project.UpdateProject;
using Core.Features.ProjectTag.DeleteProjectTag;
using Core.Features.ProjectTag.GetProjectTag;
using Core.Features.ProjectTag.GetProjectTags;
using Core.Features.Provider.AddLocation;
using Core.Features.Provider.CreateProvider;
using Core.Features.Provider.DeactivateProvider;
using Core.Features.Provider.GetProvider;
using Core.Features.Provider.GetProviders;
using Core.Features.Provider.GetProvidersProviderAdmin;
using Core.Features.Provider.ReactivateProvider;
using Core.Features.Provider.RemoveLocation;
using Core.Features.Provider.ResourceAssignment;
using Core.Features.Provider.UpdateLocation;
using Core.Features.Provider.UpdateProvider;
using Core.Features.Request.AcceptRequest;
using Core.Features.Request.CreateRequest;
using Core.Features.Request.GetPublicRequestInfo;
using Core.Features.Request.GetRequests;
using Core.Features.Request.GetSentRequests;
using Core.Features.Request.HandleActionLink;
using Core.Features.Request.RejectRequest;
using Core.Features.Resource.CheckAccountAccess;
using Core.Features.Resource.CreateResourceOnLocation;
using Core.Features.Resource.DeleteResource;
using Core.Features.Resource.GetResource;
using Core.Features.Resource.GetResourceRequestStatus;
using Core.Features.Resource.GetResources;
using Core.Features.Resource.GetResourcesProviderAdmin;
using Core.Features.Resource.PublishResource;
using Core.Features.Resource.ReprovisionAccounts;
using Core.Features.Resource.UpdateResource;
using Core.Features.ResourceRequest.CancelResourceRequest;
using Core.Features.ResourceRequest.CreateResourceRequest;
using Core.Features.ResourceRequest.GetProjectResourceRequests;
using Core.Features.ResourceRequest.GetResourceRequestAction;
using Core.Features.ResourceRequest.HandleResourceRequestAction;
using Core.Features.ResourceRequest.ResendApprovalEmail;
using Core.Features.Statistics.GetStatistics;
using Core.Features.User.GetUserPreferences;
using Core.Features.User.GetUsers;
using Core.Features.User.PromoteToProviderAdmin;
using Core.Features.User.ResetToPlatformUser;
using Core.Features.User.UpdateUserPreferences;
using Core.Features.UserInfo.GetExtendedUserInfo;
using Core.Features.UserInfo.GetUserInfo;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

using UserOrg.Shared.Extensions;
using UserOrg.Shared.FeatureManagement;

namespace Core.Composition;

public static class EndpointsExtensions
{
    public const string V2RoutePrefix = "api/v2";

    extension(IEndpointRouteBuilder app)
    {
        public IEndpointRouteBuilder MapCoreEndpoints()
        {
            // Runtime kill-switch: the host only maps this group when V2Api was on at startup, but a
            // later flip to off must 404 the routes without a restart (FeatureFlagMiddleware).
            var v2 = app.MapGroup(V2RoutePrefix).RequireAuthorization().EnabledWhen(FeatureFlags.V2Api);

            v2.MapGetProjectEndpoint();
            v2.MapGetProjectsEndpoint();
            v2.MapGetProjectsProviderAdminEndpoint();
            v2.MapGetProjectUsersEndpoint();
            v2.MapGetExtendedProjectEndpoint();
            v2.MapGetProjectTemplatesEndpoint();
            v2.MapUpdateProjectEndpoint();
            v2.MapCreateProjectTagEndpoint();
            v2.MapAssignProjectTagEndpoint();
            v2.MapUnassignProjectTagEndpoint();
            v2.MapAssignProjectUserEndpoint();
            v2.MapUnassignProjectUserEndpoint();
            v2.MapAssignProjectResourceEndpoint();
            v2.MapUnassignProjectResourceEndpoint();
            v2.MapGetValidRolesEndpoint();
            v2.MapChangeProjectUserRoleEndpoint();
            v2.MapCreateProjectEndpoint();
            v2.MapDeleteProjectEndpoint();

            v2.MapGetProviderEndpoint();
            v2.MapGetProvidersEndpoint();
            v2.MapCreateProviderEndpoint();
            v2.MapUpdateProviderEndpoint();
            v2.MapDeactivateProviderEndpoint();
            v2.MapReactivateProviderEndpoint();
            v2.MapAddLocationEndpoint();
            v2.MapUpdateLocationEndpoint();
            v2.MapRemoveLocationEndpoint();
            v2.MapAssignResourceEndpoint();
            v2.MapUpdateAssignmentEndpoint();
            v2.MapRemoveAssignmentEndpoint();

            v2.MapGetRequestsEndpoint();
            v2.MapGetSentRequestsEndpoint();
            v2.MapGetPublicRequestInfoEndpoint();
            v2.MapCreateRequestEndpoint();
            v2.MapAcceptRequestEndpoint();
            v2.MapRejectRequestEndpoint();
            v2.MapHandleActionLinkEndpoint();

            v2.MapCreateOnboardingRequestEndpoint();
            v2.MapAcceptOnboardingRequestEndpoint();
            v2.MapRejectOnboardingRequestEndpoint();
            v2.MapGetMyOnboardingRequestsEndpoint();

            v2.MapGetUserInfoEndpoint();
            v2.MapGetExtendedUserInfoEndpoint();
            v2.MapGetUsersEndpoint();
            v2.MapGetUserPreferencesEndpoint();
            v2.MapUpdateUserPreferencesEndpoint();
            v2.MapPromoteToProviderAdminEndpoint();
            v2.MapResetProviderAdminEndpoint();

            v2.MapGetLocationsEndpoint();
            v2.MapGetLocationEndpoint();
            v2.MapGetLocationsProviderAdminEndpoint();
            v2.MapGetProvidersProviderAdminEndpoint();

            v2.MapGetProjectTagsEndpoint();
            v2.MapGetProjectTagEndpoint();
            v2.MapDeleteProjectTagEndpoint();

            v2.MapGetRequestStatesEndpoint();
            v2.MapGetRequestTypesEndpoint();
            v2.MapGetOperationModesEndpoint();
            v2.MapGetLocationTypesEndpoint();
            v2.MapGetProjectDomainTypesEndpoint();
            v2.MapGetAllocationUnitTypesEndpoint();
            v2.MapGetAuthenticationTypesEndpoint();

            v2.MapGetCountriesEndpoint();

            v2.MapGetStatisticsEndpoint();

            v2.MapGetResourcesEndpoint();
            v2.MapGetResourcesProviderAdminEndpoint();
            v2.MapGetResourceEndpoint();
            v2.MapGetResourceRequestStatusEndpoint();
            v2.MapCreateResourceOnLocationEndpoint();
            v2.MapUpdateResourceEndpoint();
            v2.MapDeleteResourceEndpoint();
            v2.MapPublishResourceEndpoint();
            v2.MapCheckAccountAccessEndpoint();
            v2.MapProvisionAccountsEndpoint();

            // Resource-assignment request flow (ADR-0063/0064). Project-side routes are Owner/Manager
            // scoped in-handler (RequireProjectScope); the public token routes are anonymous (AllowAnonymous)
            // and reachable through the gateway's /api/v2/Public prefix allowlist.
            v2.MapCreateResourceRequestEndpoint();
            v2.MapGetProjectResourceRequestsEndpoint();
            v2.MapCancelResourceRequestEndpoint();
            v2.MapResendApprovalEmailEndpoint();
            v2.MapGetResourceRequestActionEndpoint();
            v2.MapHandleResourceRequestActionEndpoint();

            v2.MapGetVersionEndpoint();

            v2.MapResetAllCachesEndpoint();

            return app;
        }
    }
}