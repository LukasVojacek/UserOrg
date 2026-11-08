# Waldur.Contracts

The event contract for the Waldur sync refactor (ADR-0053/0054/0055). This is the hexagonal
seam between the pull side (`Waldur.Sync`, produces events) and the consumer side
(`Userorg.Adapter/Waldur`, applies events to the UserOrg DB). It is the exact place where
RabbitMQ slots in later, so it has **zero project references** and every payload is a plain,
JSON-serializable record.

## Contents

- **Events** (`Events/`): `WaldurIntegrationEvent` (abstract base carrying `Instance` +
  `ServiceName`) and the sealed CRUD events — `ProviderCreated/Updated`,
  `ProjectCreated/Updated`, `UserAssigned`, `UserRoleChanged`, `UserRemoved`,
  `StorageConfigurationChanged`. Taxonomy per ADR-0054/A1 (no `ProjectDeleted`; removal is the
  `IsRemoved` flag on `ProjectPayload`).
- **Payloads** (`Payloads/`): fat, event-carried-state-transfer records. Fields are enumerated
  from the old exporters (`WaldurEfpExporter`, `PuhuriExporter`).
- **Port** (`IWaldurEventPublisher`): the publish seam. The in-process implementation completes
  once the handler completed (ack = handler success, ADR-0056).
- **Hashing** (`[GenerateIdentityHash]`): every payload record carries this attribute
  (`UserOrg.Shared.Hashing`, a reusable source generator — not Waldur-specific). It generates each
  type's `ComputeIdentityHash()`, applying the ADR-0055/A2 normalization checklist at compile time
  instead of via runtime reflection. Both `Waldur API → payload` and `UserOrg DB → payload`
  mappings hash through the *same* generated method so their hashes cannot diverge. Call
  `payload.ComputeIdentityHash()` directly — there is no separate hasher type to go through.

## Hashing rules (ADR-0055/A2, binding)

1. No clock reads anywhere (audit fields are excluded from the hash via `[HashIgnore]`).
2. Truncation to DB limits happens in the *mapping*, not here.
3. GUIDs are normalized to lowercase `"D"` (dashed) form.
4. Dates are UTC, rounded to second precision.
5. Null-date conventions (`EndDate ?? MaxValue`) are applied in mapping.
6. Null and empty collections are the same value; collections are sorted deterministically. A
   collection of another `[GenerateIdentityHash]` payload is composed via each element's own
   `ComputeIdentityHash()`, not by inlining the element.
7. Strings are serialized as-is (normalization/trimming lives in mapping).
8. Decimals use the invariant culture.
9. Property order is irrelevant — properties are sorted by name before serialization.

Fields that travel for write purposes but must never affect change detection (audit timestamps,
create-only user-profile fields — ADR-0055/A1 & A3) are marked with `[HashIgnore]`.
