# Waldur.Sync — configuring instances (EFP, Puhuri, …)

The refactored Waldur sync binds a single config section, `Waldur`, whose `Instances`
array holds **one element per Waldur deployment**. Each instance gets its own HTTP client,
its own background worker, and its own SHA map — they are fully independent.

```
AddWaldurSyncModule(configuration)   // Program.cs / composition root
   └─ binds  Waldur:Instances[]      // WaldurSyncOptions
```

Only instances with `Enabled: true` are wired up and validated. A disabled or absent
instance registers nothing (no worker, no client). Enabled-instance `Name`s must be unique.

## Minimal example — two instances

`appsettings.json` (non-secret values; put `EndpointApiKey` in user-secrets / Vault):

```jsonc
{
  "Waldur": {
    "Instances": [
      {
        "Name": "EFP",                        // log/OTel tag; must be unique
        "Enabled": true,
        "BaseUrl": "https://efp.waldur.example/",
        "CustomersEndpoint":                  "/api/customers/",
        "ProjectsEndpoint":                   "/api/projects/",
        "MarketplacePublicOfferingsEndpoint": "/api/marketplace-public-offerings/",
        "MarketplaceResourcesEndpoint":       "/api/marketplace-resources/",
        "AuthorizationHeader": "Authorization",
        "AuthorizationHeaderValuePrefix": "Token",
        "EndpointApiKey": "<secret>",         // → header: "Authorization: Token <secret>"
        "Identity": {
          "ServiceName": "EFP-Waldur",        // upsert discriminator — do not reuse across instances
          "ServiceAccountUserName": "efp-svc",
          "WaldurPIEmail": "pi@efp.example"
        }
      },
      {
        "Name": "Puhuri",
        "Enabled": true,
        "BaseUrl": "https://puhuri.waldur.example/",
        "CustomersEndpoint":                  "/api/customers/",
        "ProjectsEndpoint":                   "/api/projects/",
        "MarketplacePublicOfferingsEndpoint": "/api/marketplace-public-offerings/",
        "MarketplaceResourcesEndpoint":       "/api/marketplace-resources/",
        "AuthorizationHeader": "Authorization",
        "AuthorizationHeaderValuePrefix": "Token",
        "EndpointApiKey": "<secret>",
        "Identity": {
          "ServiceName": "Puhuri-Waldur",
          "ServiceAccountUserName": "puhuri-svc",
          "WaldurPIEmail": "pi@puhuri.example"
        }
      }
    ]
  }
}
```

## Required fields (enabled instances)

Validated at startup — a missing one **fails fast**:

| Field | Notes |
|---|---|
| `Name` | Unique across enabled instances. |
| `BaseUrl` | Absolute URI. |
| `CustomersEndpoint`, `ProjectsEndpoint`, `MarketplacePublicOfferingsEndpoint`, `MarketplaceResourcesEndpoint` | Must be **relative** — combined onto `BaseUrl`. An absolute URI here is rejected (it would silently point the client at another host). |
| `AuthorizationHeader`, `EndpointApiKey` | Auth header value is `"{AuthorizationHeaderValuePrefix} {EndpointApiKey}"`. |
| `Identity.ServiceName` | The upsert-key discriminator (e.g. `EFP-Waldur`). Must differ per instance. |
| `Identity.ServiceAccountUserName` | Sync service account. |
| `Identity.WaldurPIEmail` | Non-empty — HEAppE PI email is derived from it. |

## Optional / defaulted fields

HTTP tuning (per instance): `Timeout`, `RateLimitPerSecond` (100), `RateLimitBurst`,
`RetryCount` (1), `RetryInitialBackoffMs` (200), `RetryMaxDelayMs`,
`MaxFanOutConcurrency` (4), `SyncIntervalMinutes` (30).

Identity: `ServiceProjectDomainName` (`"External"`), `ServiceAccountEmail`,
`WaldurPreferredAuthType`.

`AllocationUnitMap` — Waldur→Userorg unit conversion. Defaults for **every** instance to:
`node-hours×1 → Node-hours`, `core-hours×1000 → k-Hours`, `GB×1000 → TB-hours`.
Only override if a deployment measures units differently.

## EFP-only: HEAppE map & storage reconciliation

These are per-instance and only needed where they apply (EFP uses them; Puhuri today does not).

**`HeappeInstancesMap`** — required for any HPC resource: maps a provider to its HEAppE
instance. Key = provider (customer) GUID in **dashless `"N"` format**. The consumer throws if
an HPC resource's provider is absent here.

```jsonc
"HeappeInstancesMap": {
  "0f1e2d3c4b5a69788796a5b4c3d2e1f0": {   // provider GUID, no dashes
    "Uri": "https://heappe.efp.example/",
    "Identifier": "efp-cluster-1"
  }
}
```

**`Storage`** — EFP storage reconciliation (drives `StorageConfigPayload`). Omit entirely for
instances that don't reconcile storage (Puhuri). iRODS/staging maps are keyed by provider GUID
(same `"N"` format).

```jsonc
"Storage": {
  "EfpCoreServiceName": "EFP-Core",         // owns reconciliation-created rows
  "UseSharedIrodsForAllEntities": false,
  "SharedIrods": { "Name": "shared", "Endpoint": "https://irods.efp.example/", "AllocationAmount": 1000 },
  "IrodsInstancesMap": {
    "0f1e2d3c4b5a69788796a5b4c3d2e1f0": { "Name": "irods-1", "Endpoint": "https://irods1.efp.example/" }
  },
  "StagingAreaInstancesMap": {
    "0f1e2d3c4b5a69788796a5b4c3d2e1f0": { "Name": "staging-1", "Endpoint": "https://staging1.efp.example/" }
  }
}
```

## Enabling / disabling

Flip `Enabled` per instance. Set it `false` (or remove the element) to stop that instance's
worker without touching the others — no code change, restart to apply.

> Keep `EndpointApiKey` and any other secrets out of `appsettings.json`; supply them via
> user-secrets locally and Vault in deployed environments.
