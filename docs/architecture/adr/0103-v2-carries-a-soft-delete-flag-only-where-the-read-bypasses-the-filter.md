# ADR 0103 — A v2 response carries a soft-delete flag only where its read bypasses the soft-delete filter

- Status: Accepted
- Date: 2026-09-12
- Context: the `IsDeleted` divergence between v1 `/api/UserInfo/Extended` and v2 `/api/v2/UserInfo/Extended`,
  raised by `ParityTests.UserParityTests.Should_MatchStatusAndShape_When_ReadingExtendedUserInfo`.

## Context

Commit `2fb9d648` "feat: inactive users" shipped both halves of deactivation at once, and they differ by
design. On v1 it added `IsDeleted` to `UserInfoExtendedModel`
(`src/Core/Domain/ExtModels/UserOrg/OutputModels/Others/UserInfoExtendedModel.cs:12`, populated at
`src/Core/Application/UserOrg/Services/UserService.cs:263`). v1 needs that flag because it has no gate:
`GetBySidWithProjectsAsync` (`src/Core/Infrastructure.DataAccess/Repositories/UserRepository.cs:88`) carries
no `!IsDeleted` predicate, unlike its siblings at `:34`, `:63`, `:120`, `:126` and `:135`, so a soft-deleted
caller still gets 200 from v1 and the flag is the only way v1 can tell them.

On v2 the same commit added a deactivation *gate* instead: `CallerProvisioning.ProvisionAsync`
(`src/Modules/Core/Features/UserInfo/UseCases/CallerProvisioning.cs`) fails with
`CallerProvisioningErrors.Deactivated()` for a soft-deleted row and `UserInfoLoader.ToProblem`
(`src/Modules/Core/Features/UserInfo/UserInfoLoader.cs:28`) turns that into 403 `user.deactivated`. A v2 200
body on that endpoint could therefore only ever carry `IsDeleted: false`.

The parity suite requires a shape drop to be declared per endpoint with a reason in the caller and never
tolerated silently (`Tests/E2E/IntegrationTests/Parity/B07ParitySupport.cs:14-22`), so the divergence needs a
rule to point at rather than an ad-hoc exception.

## Decision

**A v2 response carries a soft-delete flag only where its read deliberately bypasses the soft-delete query
filter.** The rule runs in one direction: a flag implies a bypass, never the reverse. The enumeration below is
exhaustive: exactly three v2 response types declare an `IsDeleted` member, and all three are traced
(`GetProviderResponse` is built by two reads since the 2026-09-29 amendment).

| v2 read | bypasses the filter? | response | a soft-deleted row |
|---|---|---|---|
| `src/Modules/Core.DataAccess/Queries/ProjectDetailsQuery.cs:24-26` `GetProjectAsync` | yes, `IgnoreQueryFilters()` | `src/Modules/Core/Features/Project/GetProject/GetProjectResponse.cs:17` | returned, `IsDeleted: true` |
| `src/Modules/Core.DataAccess/Queries/SyncDetailsQuery.cs:36` `GetSyncDetailsAsync` | yes, `IgnoreQueryFilters()` | `src/Modules/Services/Ddi/UserOrg.Ddi/Features/Service/GetSyncDetailsEndpoint.cs:32` | returned, `IsDeleted: true` |
| `src/Modules/Core.DataAccess/Queries/ProviderDetailsQuery.cs:36-54` `GetAdministeredProvidersAsync` with `IncludeInactive` | yes, `IgnoreQueryFilters(["ProviderActive"])` only | `src/Modules/Core/Features/Provider/GetProvider/GetProviderResponse.cs:14` | returned, `IsDeleted: true`, with its non-deleted locations |
| `src/Modules/Core.DataAccess/Queries/ProviderDetailsQuery.cs:12-34` `GetProviderDetailsAsync` | **no** | `src/Modules/Core/Features/Provider/GetProvider/GetProviderResponse.cs:14` | 404 `provider.not_found`; the flag is always `false` on this read |
| `src/Modules/Core.DataAccess/Queries/UserDetailsQuery.cs:37-55` `GetExtendedBySubAsync` (self) | no | `UserInfoExtendedResponse` — no flag | 403 `user.deactivated`, gated upstream by `CallerProvisioning.ProvisionAsync` before this read runs |
| `src/Modules/Core.DataAccess/Queries/UserDetailsQuery.cs:163` `GetUsersAsync` (paged list) | no | `UserSummary` — no flag | silently absent from the page (excluded by `UserConfiguration.cs:110`); no gate, so no 403 |

The rule is per read, not per entity, because of *why* the three bypasses exist: the project and sync reads
serve a consumer that must **reconcile state it already holds**. `SyncDetailsQuery.cs:32-33` says so outright — a
deactivated user is reported with `IsDeleted = true` so the DDI sync can drop it — and a project is addressed by
short name, so a soft-deleted one must still resolve in order to say it is gone. The third,
`GetAdministeredProvidersAsync`, serves a consumer that must find deactivated rows in order to act on them (see
the Amendment). `UserInfo/Extended` is neither: it is the
caller authenticating as themselves, and a deactivated caller has nothing to reconcile, so it gates.

The v1 extended model's `IsDeleted` therefore does have a v2 home — the DDI sync read, whose `SyncUserView`
is documented as mirroring the legacy `UserInfoExtendedModel` (`src/Modules/Core.DataAccess/Queries/Contracts/SyncDetailsContracts.cs:7`)
— just not on userinfo.

The converse genuinely fails, which is why the rule is stated one-directionally. Several v2 reads bypass a
soft-delete filter and surface no flag at all: `src/Modules/Core.DataAccess/Queries/RequestDetailsQuery.cs:36-37` bypasses the project and
user filters so a request on a soft-deleted project or user still resolves its historical name (its own
comment at `:33-35` says so), `src/Modules/Core.DataAccess/Queries/UserDetailsQuery.cs:235` does the same for provider names, and
`src/Modules/Core.DataAccess/Queries/UserDetailsQuery.cs:28-34` `IsDeactivatedAsync` bypasses to feed the 403 gate. None of them reports
deletion to a caller. The rule constrains the **flag**, not the bypass.

## Consequences

- `UserInfoExtendedResponse` does not and will not carry `IsDeleted`. The v1↔v2 shape divergence is permanent,
  not provisional, and is declared as a `v1Drop` at the single call site in
  `Tests/E2E/IntegrationTests/Parity/UserParityTests.cs`
  (`Should_MatchStatusAndShape_When_ReadingExtendedUserInfo`) with this ADR as its reason.
- **`GetProviderResponse.IsDeleted` is live on one read only.** `GET ProviderAdmin/Providers?includeInactive=true`
  bypasses the named `ProviderActive` filter and reports a deactivated provider with `IsDeleted: true`
  (amendment below). `GET Providers/{providerName}` projects `p.IsDeleted` under the active provider filter,
  so there the flag is always `false`. Follow-up task **T-094** pins that, and no longer removes the field.
- A future v2 read that wants to expose a soft-delete flag has to justify the bypass first — name the consumer
  that must find deactivated rows in order to act on them, or one that reconciles state it already holds.
  Without that consumer, the read gates and the flag does not exist.

## Amendment (2026-09-29)

A third flag-carrying v2 read bypasses the filter: `GetAdministeredProvidersAsync`
(`src/Modules/Core.DataAccess/Queries/ProviderDetailsQuery.cs:36-54`), behind
`GET /api/v2/ProviderAdmin/Providers?includeInactive=true`. It ignores only the named `ProviderActive` filter
(`src/Modules/Core.DataAccess/Configurations/ProviderConfiguration.cs:119`), so the anonymous location filter
(`src/Modules/Core.DataAccess/Configurations/LocationConfiguration.cs:111`) stays in force and deleted
locations stay hidden.

Its consumer is the provider-admin portal's reactivate flow. It does not reconcile state it already holds:
it must find deactivated providers in order to reactivate them through
`POST /api/v2/Providers/{providerName}/Reactivate`. The consumer criterion is therefore widened from "a
consumer that reconciles state it already holds" to **"a consumer that must find deactivated rows in order to
act on them, as well as one reconciling state it already holds"**.

Consequences of the amendment:

- `GetProviderResponse.IsDeleted` stays in the shared response type: live on `GET ProviderAdmin/Providers`,
  always `false` on `GET Providers/{providerName}`. The table row and the Consequences bullet above are
  rewritten accordingly.
- T-094 is narrowed from removing the field to pinning it always `false` on the single-provider read.
  `ProviderDetailsQueryTests.Should_ReturnNull_When_ProviderDeactivated` already pins that the single read
  keeps hiding a deactivated provider.
- With `includeInactive=true` a deactivated provider is returned with its non-deleted locations, while
  `GET /api/v2/ProviderAdmin/Locations` still omits them.

Rejected: dropping `includeInactive` (loses the requested inactive listing), and adding the flag with no ADR
change (leaves this ADR false against the code).
