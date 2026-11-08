# UserOrg

Organisation, project, and access-management backend. Clients authenticate at an external Identity Provider; the Gateway issues a short-lived **internal token** that backend services (Core) trust for identity and authorization.

## Language

### Identity & access

**IdpId**:
The caller's identity as asserted by the Identity Provider — the `sub` claim. Always present on an authenticated request.
_Avoid_: sub (in code prose), identity, user id (it is *not* the domain UserId)

**UserId**:
The Core domain `User` aggregate's own identity (a GUID, the database primary key). Resolved from an `IdpId`; not the same thing as the `IdpId`.
_Avoid_: idpId, sub, account id

**Natural key**:
The human-meaningful business identifier of an aggregate — `ProjectShortName`, `UserName`, `ProviderName`, `LocationName`, `ResourceName`, `TagName`, `CountryCode`. The identifier a client names an entity by. Distinct from the **Surrogate id**. Some natural keys are immutable (`ProjectShortName`, `ResourceName`); a few can change at the source (`UserName` tracks the IdP `PreferredUsername`).
_Avoid_: business id, name (unqualified)

**Surrogate id**:
The GUID primary key EF assigns to an aggregate (`UserId`, `ProjectId`, `ProviderId`, `RequestId`, …). An internal database concern: it is the stable join target inside the schema but is **never** required as an operation parameter at the API — callers address entities by **Natural key** (or `IdpId` for a User).
_Avoid_: database id, primary key (in API prose), technical id

**Internal token**:
The short-lived JWT the Gateway mints per request and forwards to backend services in place of the original client token. Carries `IdpId` (`sub`), the effective role, and the `projectRoles` claim. Backends trust it without calling the Identity Provider.
_Avoid_: access token, bearer token, client token (those are the *original* IdP token, preserved as `X-Original-Authorization`)

**projectRoles claim**:
A claim on the internal token mapping each role name to the project short-names in which the caller holds that role, e.g. `{ "ProjectUser": ["projectA"], "ProjectManager": ["projectB"] }`. Absence of a project means no access.
_Avoid_: permissions claim, scope claim

**Caller**:
The authenticated principal behind a request, resolved from the internal-token claims — its `IdpId`, effective role, and `projectRoles`. Present on every request that passes the authorization gate.
_Avoid_: current user, principal, requester

**Caller scope**:
The set of projects a caller is allowed to see, derived from the `projectRoles` claim.
_Avoid_: tenancy, project filter

**Read scope tier**:
How much data a read endpoint returns, by precedence: **Admin** (`Role` = the admin role) sees everything; a **provider identity** (the `ProviderName` claim is present) sees only its provider's data; otherwise the caller sees their **caller scope**. The scoping key for the provider tier is the `ProviderName` claim — the same key writes use — **never** the `ProviderAdmin` role name. Endpoints callable by more than one tier branch on this explicitly in the handler while returning the same response shape.
_Avoid_: scoping a provider's reads by the ProviderAdmin role name; hiding the tier choice in the query layer

**ApiAudience**:
A documentation-grouping label (`User` / `ProviderAdmin` / `Admin`) deciding which OpenAPI document a v2 endpoint is *described* in. It does **not** decide who may call the endpoint (the Gateway's RBAC does) nor how data is scoped (the **read scope tier** does). A v2 endpoint with no audience tag defaults to `User`.
_Avoid_: treating the audience tag as authorization or as a data-scoping key

**Token audience**:
The `aud` claim of the IdP token a caller presents to the Gateway: the services the token was issued for. With strict audience validation on, the Gateway accepts a user token only when every audience it carries is one the Gateway is configured to accept. Unrelated to **ApiAudience**.
_Avoid_: audience (unqualified), confusing it with ApiAudience

### Project membership & roles

**Project membership**:
The fact that a User is attached to a Project, independent of what they may do there — the `AssignedUserToProject` link. A membership can be active or removed (soft-deleted). Carries no permission by itself.
_Avoid_: assignment (ambiguous with resource/tag assignment), project access

**Project role**:
The role a member holds *within one Project* (e.g. ProjectOwner, ProjectManager, ProjectMember), distinct from the caller's system role. Persisted as a `UserPermission` row scoped to that project (`ProjectId` set). It is the sole source the Gateway reads to build the **projectRoles claim**; Core writes it but trusts the claim for reads.
_Avoid_: system role (a platform-wide role, see **System role**; the row holding it is the **System permission**), permission

**Role hierarchy**:
A total ordering of roles by level. A member may only grant or change another member to a role *strictly below* their own level, and each Project has at most one ProjectOwner. Enforced in Core because it is a *relative* rule (requester level vs target level) the Gateway's permission gate cannot express.
_Avoid_: role rank, seniority

### Project tagging

**Tag**:
A label in a single **global, shared catalogue**, identified by its **name** (unique across the system). One catalogue entry is shared by every project that uses it — tagging two projects "biology" references the *same* Tag, it does not create two. Creating a Tag (minting a new catalogue entry) is distinct from assigning an existing one to a project.
_Avoid_: per-project tag, label, category

**Tag assignment**:
The association between a Project and an existing Tag (the M:N join). Assigning or unassigning never creates or deletes the Tag itself — only the link. Assigning a name that has no Tag in the catalogue fails; it does not auto-create the Tag.
_Avoid_: tagging (ambiguous between create and assign)

**ProjectTag**:
The persistence/code name for a catalogue **Tag** entity (EF type, aggregate collection). In domain prose prefer **Tag**; "ProjectTag" does *not* mean a tag scoped to one project.
_Avoid_: using "ProjectTag" to imply per-project ownership

### Resources & allocation

**Resource**:
A shared compute or storage resource (HPC, cloud, storage, …) that can be allocated to Projects. It lives in a catalogue and is identified by its **globally-unique `ResourceName`** — the identifier a client addresses it by. A Resource is **shared M:N with Providers** (real data shows one resource offered under more than one Provider, e.g. `OPEN-33-82` under both IT4Innovations and LUMI), so it has no single owning Provider; allocation to Locations and Projects is likewise M:N. It is *not* scoped to a single Project (the code/EF name `ProjectResource` notwithstanding). See ADR-0024.
_Avoid_: project resource (the code/EF name `ProjectResource` does **not** mean a resource scoped to one project), asset, owned by exactly one provider, `(ProviderName, ResourceName)` composite identity

**Resource assignment**:
The allocation linking a **Resource** to a Project at a **Location**, carrying an allocation amount and unit. One Resource has many assignments.
_Avoid_: project resource assignment (code name `ProjectResourceAssignment`), allocation (ambiguous with the amount field)

**HEAppE project**:
The HEAppE-side allocation record, identified by its **accounting string**. It corresponds 1:1 to a userorg **Resource** — the accounting string *is* the `ResourceName`. A single HEAppE instance hosts many HEAppE projects; one `ListAvailableClusters`/`Management/Clusters` response carries all of them (`NodeType.Projects[].AccountingString`). **Not** a userorg **Project** (the access-management aggregate keyed by `ProjectShortName`): a userorg Project assigns several Resources, each of which is a separate HEAppE project, possibly on different instances.
_Avoid_: project (unqualified — always say "HEAppE project" vs "userorg Project"), accounting string used as a synonym for userorg Project

### Providers & locations

**Provider**:
A "LEXIS Provider" — an organisation that offers compute/storage capacity. An aggregate identified by its **name**, owning a set of **Locations**. Can be public or private and can be deactivated (soft-deleted) without being erased.
_Avoid_: vendor, supplier, tenant

**Location**:
A child of a **Provider** representing an **HPC compute endpoint** (a cluster location reachable at an endpoint URL) — *not* a geographic address. Belongs to exactly one Provider and is created, updated, and removed only through it.
_Avoid_: site, address, region, datacentre (it is the compute endpoint, not where it physically sits)

**Provider identity** (write actor):
A provider acting on itself through a **provider service key** (API-key auth). Authorised to manage only **its own** Provider and that Provider's Locations — never another Provider, and never to create a new top-level Provider. Identified by its `ProviderName` (carried on the internal token). Contrast with **Admin**, who manages any Provider and is the only actor that may create one.
_Avoid_: provider owner, provider manager, operator, ProviderAdmin (that system-role name is not the write-scoping mechanism — the service-key identity is)

**Administered providers**:
The providers a caller administers: every provider for a platform admin (read scope `All`), the caller's own provider for a provider identity (read scope `Provider`), none otherwise. Listed by `GET /api/v2/ProviderAdmin/Providers`.
_Avoid_: my providers, managed providers, owned providers

**Name twins**:
Two or more **Provider** rows with the same name, at most one of them active. `IX_Provider_Name` is unique among active rows only, so only legacy data holds them (ADR-0027).
_Avoid_: duplicates, clones

**Twin resolution order**:
How a by-name provider write and the administered-provider list rank **Name twins**: the active row first, then `ModifiedAt` descending (NULL last), then `CreatedAt` descending, then the surrogate id.
_Avoid_: priority, preference

### Resource validity audit

**Resource validity audit**:
A read-only cross-check that, for every currently-valid **Resource** exposed on a HEAppE instance, the HEAppE **project record** for that resource exists and carries the **same validity period** userorg holds for the Resource. It runs on a schedule and can be triggered on demand by an Admin. It **reports** drift; it never corrects it (contrast with the provider-export flow, which *creates and updates* HEAppE projects).
_Avoid_: sync, reconciliation (both imply writing back — the audit only reads and reports)

**Audit target**:
One (**Resource**, HEAppE instance) pair the audit verifies. Only resources assigned at **HPC or Quantum** locations are in scope — the same location types HEAppE projects are ever provisioned for; resources at other location types are never targets even if a HEAppE URL is present. A Resource reachable on several HEAppE instances produces several targets; the same instance reached twice through two Locations is **one** target (deduplicated by resource name + HEAppE URL).
_Avoid_: resource (a target is narrower — it is a resource *at one instance*), location

**Audit finding**:
A target whose outcome is not **Ok**, in one of three kinds: **Missing** (the instance has no project record for the resource name), **ValidityMismatch** (a record exists but its start/end differ from userorg's, compared at whole-day UTC granularity), **CheckFailed** (the target could not be verified — instance unreachable, missing credentials, or an unexpected error). Only findings reach the notification; a clean run notifies nothing.
_Avoid_: error (CheckFailed is a finding, not an exception), mismatch (that is only one of the three kinds)

### Requests & onboarding

**Request**:
A pending ask, raised by a User against a Project, to be granted something there. It moves through a state machine (Pending → … → Accepted | Rejected) and is settled by **request handling**. Accepted and Rejected are terminal.
_Avoid_: invitation, application, ticket

**Self-onboarding**:
A **Request** whose fulfilment is the requester themselves joining the Project as a member. Distinct from a *resource-assignment* request, which asks for a compute/storage resource to be attached to the Project and is settled by a different (provider-side) path.
_Avoid_: invite, join request (ambiguous with membership)

**Onboarding request**:
The v2 model of **self-onboarding** as its own concept: a standalone aggregate (not part of the Project) representing one User's pending ask to join one Project as a member. It references the Project by its **Natural key** (short name) and the requester by their **IdpId** + email — never by a **Surrogate key**. Lifecycle Pending → Accepted | Rejected (terminal); accepting it grants the membership. It deliberately carries none of the legacy **Request**'s resource/provider/type/**action link** baggage. The shared **Request** term still names the legacy aggregate that the resource-assignment path keeps using.
_Avoid_: invitation, application, membership (that is the granted result, not the ask)

**Request handling**:
Settling a pending **Request** — *accepting* it (which performs the requested grant, e.g. adds the member) or *rejecting* it. A handled Request is terminal and cannot be handled again.
_Avoid_: approval (covers only the accept half), processing

**Action link**:
A single-use, time-limited tokenised URL emailed to a request approver that lets them handle a **Request** without authenticating. Using one link invalidates all sibling links for the same Request.
_Avoid_: magic link, invite link, callback

### System roles (platform-wide)

**System role**:
A platform-wide role whose authority spans all projects, independent of project membership — currently Platform Administrator (`Admin`) and Provider Administrator (`ProviderAdmin`). The Gateway does not constrain a system role to specific projects; fine-grained scoping is the API's responsibility (see **Read scope tier**).
_Avoid_: global role, super-admin

**System permission**:
The one `UserPermission` row per User with `ProjectId` null, holding the user's **System role** (Admin, ProviderAdmin) or the default User role. Changed in place on a role change, never replaced. Contrast with **Project role** (same table, `ProjectId` set).
_Avoid_: system role row, platform permission

### HPC / HEAppE

**HEAppE instance**:
A single HEAppE endpoint, identified by its URL. One instance hosts **multiple Clusters** — the URL is not cluster-specific.

**Cluster**:
A named HPC machine reachable through a HEAppE instance (e.g. "Barbora", "Karolina"). Several Clusters can share one HEAppE instance URL.

**Location / LocationName** (HEAppE binding):
A project's link to a specific Cluster on a HEAppE instance. The two systems name the same Cluster differently, so a link corresponds to a Cluster by **either** of two equivalent values: `LocationName` equal to the Cluster's name, **or** the **login host** of the Location's endpoint matching the Cluster's master-node host (a substring match, kept permissive for legacy reasons). The endpoint's port is a connection detail, **not** part of cluster identity — the host is compared port-insensitively (`login.x.bg:2224` and `login.x.bg` are the same Cluster). A link whose Cluster is absent from the instance's returned catalog yields an unavailable (empty) entry.

**NodeType**:
A named queue configuration on a Cluster (its queue, core and walltime limits) that a job targets. One Cluster exposes several NodeTypes.
_Avoid_: queue

**Aggregation**:
An optional grouping a NodeType may belong to. A NodeType **without** an Aggregation is a normal, valid case — its absence is empty (null), never an error.
_Avoid_: cluster node type aggregation

**Partition**:
A Location's record of one NodeType available on its Cluster, identified by the NodeType name plus its Aggregation. The Aggregation part is absent when the NodeType has no Aggregation.
_Avoid_: LocationPartition, queue partition

**Kill-switch**:
A provider administrator's freeze of the command templates on one Location, used during a security incident and lifted afterwards. A freeze never changes what operators configured — it only changes what the platform currently allows.

**Stored state**:
The Active/Inactive state an operator set on a command template for a Location. It is the operator's intent and the kill-switch never overwrites it.
_Avoid_: real state, DB state

**Effective state**:
What the platform allows for a command template right now — the Stored state after the kill-switch has been taken into account. A frozen template has an effective state of Frozen while its stored state stays whatever the operator last chose.

**Frozen**:
The effective state of a command template the kill-switch currently blocks on a Location. The same template can be Frozen on one Location and usable on another.

**Whitelist**:
The set of command templates a provider administrator has approved to keep running on a Location while that Location is frozen. It is standing configuration, prepared ahead of any incident: it outlives every freeze and lift, and only an administrator editing it changes it.
_Avoid_: exception list, allowlist, temporary whitelist

**Lockdown**:
A freeze that ignores the Location's Whitelist, blocking every command template. The Whitelist is not erased — it is set aside for the duration and applies again once the lockdown ends.
_Avoid_: hard freeze, full kill

**TemplatesInfo**:
The composed view of the templates available to a project, built by querying every HEAppE instance the project links to and merging the results. A single build may draw on **multiple** HEAppE instances at once.

**Degraded build**:
A TemplatesInfo build in which **at least one** HEAppE instance failed to answer. A degraded build is never cached.

**Partial degradation**:
A degraded build where some HEAppE instances answered with data and at least one failed. The data that came back is usable on its own; the failed instance simply contributes nothing.

**Total degradation**:
A degraded build where **every** queried HEAppE instance failed for a HEAppE-side reason (unreachable, unauthorized, timeout, upstream error, or hydration skeleton). Distinct from a project that genuinely has no templates (a build that succeeded and is empty), and distinct from an **internal precondition failure** — which is not a degraded build at all.
_Avoid_: outage (reserve for infrastructure-level wording)

**Hydration window**:
The period (~2 minutes) after a HEAppE instance wakes up during which it answers requests but returns skeleton/empty data instead of real Clusters. An instance mid-hydration is treated as degraded, not as "no data".

**Internal precondition failure**:
A TemplatesInfo build failing because of *our* side rather than any HEAppE instance — currently a failed read of the project's active command templates from the Heappe integration database. It is not a Degraded build: the remote HEAppE instances were never the problem (and may never be queried), so it is reported as our fault, not as HEAppE being unavailable.
_Avoid_: degraded, total degradation (reserve those for HEAppE-side failures)

**RBAC seed**:
The `roleManagmentRBAC.json` file at the repository root — the versioned source of permissions, endpoint mappings, and roles the Gateway imports into the RBAC store. One file for all environments; baked into the Gateway image as `rbac/seed.json`.
_Avoid_: RBAC config (ambiguous with runtime store), permission file

**Fail-closed mapping gate**:
The Gateway rule that a route with **no** entry in the endpoint-permission map is denied (403 "no permission mapping for this endpoint") for every authenticated caller. A route mapped with an **empty** permission list is the opposite: allowed for any authenticated caller.
_Avoid_: fail-open (the Gateway does not fail open on unmapped routes)

**Additive seed reconciliation**:
The startup behavior (ADR-0060) where the Gateway adds seed entries missing from the RBAC store — permissions by key, endpoint mappings by path template + HTTP method, roles by name — and never modifies or deletes existing ones. Changed or removed seed entries propagate only through a manual RoleManagement UI import with overwrite.
_Avoid_: seed sync (implies bidirectional or destructive), full reconciliation

### Logging

**Normal mode**:
The default logging state (ADR 0112): the level switch at BaseLevel Warning and the **framework categories** held at their Warning **category floor**.
_Avoid_: production mode, quiet mode

**Auto step-up**:
StepUpLogging's automatic raise of the level switch to Information after an Error, bounded by DurationSeconds 60 and MaxContinuousStepUpSeconds 600. The framework categories stay at their Warning category floor, and it never goes to Debug.
_Avoid_: debug step-up, escalation

**Diagnostic mode**:
The library's `SerilogStepUp:Mode=Diagnostic`, set at process start, and the only state in which Debug is emitted. The switch sits at DiagnosticLevel Debug and the category floors are lifted; the exporter categories stay at Warning, because they are root overrides. After `SerilogStepUp:DiagnosticDurationMinutes` (default 30, library-validated 1 to 120) the host runs as Auto at BaseLevel with auto step-up working. It re-arms on every start while `Mode=Diagnostic` is set, so the operator sets `SerilogMode` back to Auto after the session.
_Avoid_: debug mode (unqualified), verbose mode, `DiagnosticLogging:Enabled`

**Framework categories**:
The Serilog SourceContext prefixes of framework and third-party code (Polly, HttpClient, ASP.NET Core, EF Core, YARP, `Serilog.AspNetCore.RequestLoggingMiddleware`, Duende, AspNetCoreRateLimit, Ogham and the catch-alls `Microsoft` and `System`), each a **category floor** at Warning. The full list is `FrameworkLevelOverrides.FrameworkCategories`.
_Avoid_: noise categories, infrastructure logs

**Category floor**:
A StepUpLogging 5.x `SerilogStepUp:CategoryFloors` entry. An event below its floor is not exported but held in the **pre-error buffer**, and Diagnostic mode lifts it.
_Avoid_: override (that is a **root override**)

**Root override**:
A Serilog `Serilog:MinimumLevel:Override` entry. An event below it is dropped before any sink, so it never reaches the pre-error buffer, in every mode.
_Avoid_: floor

**Lifecycle category**:
`Microsoft.Hosting.Lifetime`, a root override at Information and listed in `SerilogStepUp:AlwaysExportCategories`, so its start and stop lines are exported in Normal mode.
_Avoid_: startup noise

**Pre-error buffer exclusions**:
`SerilogStepUp:PreErrorBufferExcludeCategories`, the framework categories plus EF Core `Database.Command`, `ChangeTracking`, `Query` and `Update` and `Microsoft.AspNetCore.Hosting.Diagnostics`. Their events below Warning are never held, so an Error flush does not export them; Diagnostic mode lifts them to Debug like any framework category.
_Avoid_: diagnostic-exempt categories, noisy categories

**Exporter categories**:
The OTLP exporters' HttpClient categories (`System.Net.Http.HttpClient.OtlpLogExporter`, `OtlpMetricExporter`, `OtlpTraceExporter`), root overrides at Warning in every mode.
_Avoid_: telemetry noise

**Request summary**:
The one StepUpLogging `IsRequestSummary` event per request, written through the bypass logger in every mode, the public token routes included with the token segment redacted in the path, `RequestPath` and `http.target`.
_Avoid_: access log, completion event (that is the Serilog `HTTP ... responded` event, a framework category)

**Pre-error buffer**:
StepUpLogging's per-trace ring buffer of **held events** (at most `PreErrorBufferSize` 50 per trace, `PreErrorMaxContexts` 256 traces, LRU), exported once when an Error or Fatal occurs in the trace and dropped on dispose.
_Avoid_: error buffer, log cache

**Held event**:
An event at or above StepUpLevel that the level switch or a category floor did not export and the pre-error buffer keeps.
_Avoid_: buffered log (unqualified)

**Production logging defaults**:
The SerilogStepUp level, duration, summary, capture, pre-error buffer and ExcludePaths values `ProductionLoggingDefaults` ships in code as the lowest-priority configuration source. A mounted appsettings.json or `SerilogStepUp__*` variables override them.
_Avoid_: appsettings defaults
