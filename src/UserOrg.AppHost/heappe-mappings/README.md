# HEAppE WireMock mappings (E2E, ADR-0051)

Static WireMock.Net mappings that stand in for the external HEAppE HTTP API during
E2E tests. Loaded because the `heappe-mock` resource is registered with
`WithMappingsPath(<this folder>)` + `WithReadStaticMappings()` (see
`../WireMockExtensions.cs`). One `*.json` file per stubbed request.

These twelve mappings answer every HEAppE call the create-resource flow makes (every call reaches the mock
through the NexusApi proxy prefix `/api/heappe/{identifier}`; the `*/heappe/...` patterns match both the bare
path and the proxied one):

| file | method | path | what it answers |
|---|---|---|---|
| `100-health.json` | GET | `/heappe/Health` | health check, `Version` string |
| `110-auth-types.json` | GET | `/heappe/Dictionary/GetClusterAuthenticationCredentialsAuthTypes` | allowed auth types |
| `120-clusters.json` | GET | `/heappe/Management/Clusters` | the one mock cluster |
| `130-projects-by-accounting-strings.json` | GET | `/heappe/Management/ProjectsByAccountingStrings` | empty list, keeps the flow on the create branch |
| `140-create-project.json` | POST | `/heappe/Management/Project` | the created project |
| `150-project-assignment-to-clusters.json` | GET | `/heappe/Management/ProjectAssignmentToClusters` | empty list |
| `160-create-project-assignment-to-cluster.json` | POST | `/heappe/Management/ProjectAssignmentToCluster` | the created assignment |
| `170-cluster-node-type-aggregations.json` | GET | `/heappe/Management/ClusterNodeTypeAggregations` | the one mock aggregation |
| `180-project-cluster-node-type-aggregations.json` | GET | `/heappe/Management/ProjectClusterNodeTypeAggregations` | empty list |
| `190-create-project-cluster-node-type-aggregation.json` | POST | `/heappe/Management/ProjectClusterNodeTypeAggregation` | the created project/aggregation link |
| `200-generate-secure-shell-key.json` | POST | `/heappe/Management/GenerateSecureShellKey` | one generated key pair |
| `210-test-cluster-access-for-account.json` | PUT | `/heappe/Management/TestClusterAccessForAccount` | `IsClusterAccessible: true` |

Three more stand in for the NexusApi registry and token endpoint on the token exchange flag-on path (E2E runs flag off):

| file | method | path | what it answers |
|---|---|---|---|
| `300-nexus-token.json` | POST | `/api/v1/auth/token` | a bearer token with `expires_in` |
| `310-nexus-heappe-instances.json` | GET | `/api/v1/heappe` | `heappe-mock` and `heappe` enabled, `heappe-off` disabled |
| `320-nexus-clients.json` | GET | `/api/v1/clients` | the Keycloak client of `heappe-mock` and `heappe` |

`900-catchall-get.json` stays the floor for `ClusterInfoCacheWarmer` and
`HeappeResourceAuditWorker` and must stay GET-only — a `POST`/`PUT` request that hits no
mapping above must 404, not be answered by the catch-all.

Two invariants the handlers enforce:
- `MasterNodeName` in `120-clusters.json` must contain every host the mock is reached on
  (`localhost`, `127.0.0.1`): `InitializeHeappeHandler` passes the HEAppE URL as the login
  node and `ClusterEndpointMatcher` does a substring match against `MasterNodeName`.
- `GET /heappe/Health` must report `Version >= 6.4.0`, or `HeappeService` refuses the
  auth-type call.

The mapping-contract test in `Tests/Modules/Integrations/Heappe.UnitTests/Mappings/` is
the guard: it loads this folder into an in-process WireMock and asserts every row above
is answered below priority 900, and that the catch-all does not answer an unmapped POST.

Format reference: https://github.com/wiremock/WireMock.Net/wiki/Admin-API-Reference
