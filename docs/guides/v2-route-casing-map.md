# v2 route casing map

**Audience:** every v2 caller (portal UI, service-to-service clients).
**Change:** T-242. Every literal segment of every v2 route is PascalCase (ADR-0020). The old spellings are
gone, and there are no aliases.

Route parameters keep their names. Matching ignores case, so a call that only differs in case still
works. The 29 rows whose old path has a hyphen (`onboarding-requests`, `provider-admin`, `valid-roles`, ...)
return 404 on the old spelling and must move. The other 50 rows change case only.

The table has one row per (method, path) operation whose path key changed between the `API_v2-user.json`,
`API_v2-provideradmin.json` and `API_v2-admin.json` snapshots on `main` (`e073055e`) and on this branch. Rows
from the three documents are deduplicated and paired by `operationId`. The five DDI routes
(`/api/v2/DataStaging/*`, `/api/v2/Service/*`) were already PascalCase and are not listed.

79 operations. One of them, the per-project onboarding request list, was removed afterwards by ADR-0107 and
keeps its row with its replacement. `GET /api/v2/ProviderAdmin/Providers`, added later (T-USR-2), was
PascalCase from its first release, has no old spelling and so has no row. So was
`GET /api/v2/ProviderAdmin/Providers/{providerName}/Resources`, the provider-admin copy of the resource list.

| method | old path | new path |
|---|---|---|
| GET | `/api/v2/allocation-unit-types` | `/api/v2/AllocationUnitTypes` |
| GET | `/api/v2/authentication-types` | `/api/v2/AuthenticationTypes` |
| DELETE | `/api/v2/caches` | `/api/v2/Caches` |
| GET | `/api/v2/countries` | `/api/v2/Countries` |
| GET | `/api/v2/location-types` | `/api/v2/LocationTypes` |
| GET | `/api/v2/locations` | `/api/v2/Locations` |
| GET | `/api/v2/operation-modes` | `/api/v2/OperationModes` |
| GET | `/api/v2/project-domain-types` | `/api/v2/ProjectDomainTypes` |
| GET | `/api/v2/project-tags` | `/api/v2/ProjectTags` |
| DELETE | `/api/v2/project-tags/{tagName}` | `/api/v2/ProjectTags/{tagName}` |
| GET | `/api/v2/project-tags/{tagName}` | `/api/v2/ProjectTags/{tagName}` |
| GET | `/api/v2/projects` | `/api/v2/Projects` |
| POST | `/api/v2/projects` | `/api/v2/Projects` |
| DELETE | `/api/v2/projects/{projectShortName}` | `/api/v2/Projects/{projectShortName}` |
| GET | `/api/v2/projects/{projectShortName}` | `/api/v2/Projects/{projectShortName}` |
| PUT | `/api/v2/projects/{projectShortName}` | `/api/v2/Projects/{projectShortName}` |
| GET | `/api/v2/projects/{projectShortName}/extended` | `/api/v2/Projects/{projectShortName}/Extended` |
| GET | `/api/v2/projects/{projectShortName}/onboarding-requests` | removed by ADR-0107, use `/api/v2/Users/Me/OnboardingRequests?Direction=Received&ProjectShortName=` |
| POST | `/api/v2/projects/{projectShortName}/onboarding-requests` | `/api/v2/Projects/{projectShortName}/OnboardingRequests` |
| PUT | `/api/v2/projects/{projectShortName}/onboarding-requests/{slug}/accept` | `/api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Accept` |
| PUT | `/api/v2/projects/{projectShortName}/onboarding-requests/{slug}/reject` | `/api/v2/Projects/{projectShortName}/OnboardingRequests/{slug}/Reject` |
| DELETE | `/api/v2/projects/{projectShortName}/providers/{providerName}/resources/{resourceName}` | `/api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` |
| PUT | `/api/v2/projects/{projectShortName}/providers/{providerName}/resources/{resourceName}` | `/api/v2/Projects/{projectShortName}/Providers/{providerName}/Resources/{resourceName}` |
| GET | `/api/v2/projects/{projectShortName}/resource-requests` | `/api/v2/Projects/{projectShortName}/ResourceRequests` |
| POST | `/api/v2/projects/{projectShortName}/resource-requests` | `/api/v2/Projects/{projectShortName}/ResourceRequests` |
| PUT | `/api/v2/projects/{projectShortName}/resource-requests/{slug}/cancel` | `/api/v2/Projects/{projectShortName}/ResourceRequests/{slug}/Cancel` |
| POST | `/api/v2/projects/{projectShortName}/resource-requests/{slug}/resend-approval` | `/api/v2/Projects/{projectShortName}/ResourceRequests/{slug}/ResendApproval` |
| POST | `/api/v2/projects/{projectShortName}/tags` | `/api/v2/Projects/{projectShortName}/Tags` |
| DELETE | `/api/v2/projects/{projectShortName}/tags/{tagName}` | `/api/v2/Projects/{projectShortName}/Tags/{tagName}` |
| PUT | `/api/v2/projects/{projectShortName}/tags/{tagName}` | `/api/v2/Projects/{projectShortName}/Tags/{tagName}` |
| GET | `/api/v2/projects/{projectShortName}/templates` | `/api/v2/Projects/{projectShortName}/Templates` |
| GET | `/api/v2/projects/{projectShortName}/users` | `/api/v2/Projects/{projectShortName}/Users` |
| DELETE | `/api/v2/projects/{projectShortName}/users/{idpUserId}` | `/api/v2/Projects/{projectShortName}/Users/{idpUserId}` |
| PUT | `/api/v2/projects/{projectShortName}/users/{idpUserId}` | `/api/v2/Projects/{projectShortName}/Users/{idpUserId}` |
| PUT | `/api/v2/projects/{projectShortName}/users/{idpUserId}/role` | `/api/v2/Projects/{projectShortName}/Users/{idpUserId}/Role` |
| GET | `/api/v2/projects/{projectShortName}/valid-roles` | `/api/v2/Projects/{projectShortName}/ValidRoles` |
| GET | `/api/v2/provider-admin/locations` | `/api/v2/ProviderAdmin/Locations` |
| GET | `/api/v2/provider-admin/projects` | `/api/v2/ProviderAdmin/Projects` |
| GET | `/api/v2/providers` | `/api/v2/Providers` |
| POST | `/api/v2/providers` | `/api/v2/Providers` |
| DELETE | `/api/v2/providers/{providerName}` | `/api/v2/Providers/{providerName}` |
| GET | `/api/v2/providers/{providerName}` | `/api/v2/Providers/{providerName}` |
| PUT | `/api/v2/providers/{providerName}` | `/api/v2/Providers/{providerName}` |
| POST | `/api/v2/providers/{providerName}/locations` | `/api/v2/Providers/{providerName}/Locations` |
| DELETE | `/api/v2/providers/{providerName}/locations/{locationName}` | `/api/v2/Providers/{providerName}/Locations/{locationName}` |
| GET | `/api/v2/providers/{providerName}/locations/{locationName}` | `/api/v2/Providers/{providerName}/Locations/{locationName}` |
| PUT | `/api/v2/providers/{providerName}/locations/{locationName}` | `/api/v2/Providers/{providerName}/Locations/{locationName}` |
| POST | `/api/v2/providers/{providerName}/locations/{locationName}/assignments` | `/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments` |
| DELETE | `/api/v2/providers/{providerName}/locations/{locationName}/assignments/{slug}` | `/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` |
| PUT | `/api/v2/providers/{providerName}/locations/{locationName}/assignments/{slug}` | `/api/v2/Providers/{providerName}/Locations/{locationName}/Assignments/{slug}` |
| POST | `/api/v2/providers/{providerName}/reactivate` | `/api/v2/Providers/{providerName}/Reactivate` |
| GET | `/api/v2/providers/{providerName}/resources` | `/api/v2/Providers/{providerName}/Resources` |
| POST | `/api/v2/providers/{providerName}/resources` | `/api/v2/Providers/{providerName}/Resources` |
| DELETE | `/api/v2/providers/{providerName}/resources/{resourceName}` | `/api/v2/Providers/{providerName}/Resources/{resourceName}` |
| GET | `/api/v2/providers/{providerName}/resources/{resourceName}` | `/api/v2/Providers/{providerName}/Resources/{resourceName}` |
| PUT | `/api/v2/providers/{providerName}/resources/{resourceName}` | `/api/v2/Providers/{providerName}/Resources/{resourceName}` |
| POST | `/api/v2/providers/{providerName}/resources/{resourceName}/publish` | `/api/v2/Providers/{providerName}/Resources/{resourceName}/Publish` |
| POST | `/api/v2/public/action-links/{actionLink}` | `/api/v2/Public/ActionLinks/{actionLink}` |
| GET | `/api/v2/public/requests/by-link/{actionLink}` | `/api/v2/Public/Requests/ByLink/{actionLink}` |
| GET | `/api/v2/public/resource-request-actions/{token}` | `/api/v2/Public/ResourceRequestActions/{token}` |
| POST | `/api/v2/public/resource-request-actions/{token}` | `/api/v2/Public/ResourceRequestActions/{token}` |
| GET | `/api/v2/request-states` | `/api/v2/RequestStates` |
| GET | `/api/v2/request-types` | `/api/v2/RequestTypes` |
| GET | `/api/v2/requests` | `/api/v2/Requests` |
| POST | `/api/v2/requests` | `/api/v2/Requests` |
| GET | `/api/v2/requests/sent` | `/api/v2/Requests/Sent` |
| PUT | `/api/v2/requests/{slug}/accept` | `/api/v2/Requests/{slug}/Accept` |
| PUT | `/api/v2/requests/{slug}/reject` | `/api/v2/Requests/{slug}/Reject` |
| GET | `/api/v2/resource-requests/{requestId}` | `/api/v2/ResourceRequests/{requestId}` |
| GET | `/api/v2/statistics` | `/api/v2/Statistics` |
| GET | `/api/v2/userinfo` | `/api/v2/UserInfo` |
| GET | `/api/v2/userinfo/extended` | `/api/v2/UserInfo/Extended` |
| GET | `/api/v2/users` | `/api/v2/Users` |
| GET | `/api/v2/users/me/onboarding-requests` | `/api/v2/Users/Me/OnboardingRequests` |
| GET | `/api/v2/users/me/preferences` | `/api/v2/Users/Me/Preferences` |
| PUT | `/api/v2/users/me/preferences` | `/api/v2/Users/Me/Preferences` |
| DELETE | `/api/v2/users/{idpUserId}/provider-admin` | `/api/v2/Users/{idpUserId}/ProviderAdmin` |
| PUT | `/api/v2/users/{idpUserId}/provider-admin/{providerName}` | `/api/v2/Users/{idpUserId}/ProviderAdmin/{providerName}` |
| GET | `/api/v2/version` | `/api/v2/Version` |
