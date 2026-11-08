# ADR 0010 - Nullable value objects map to nullable columns without sentinels

- Date: 2026-06-08
- Status: accepted

## Decision

When a domain value object maps to a **nullable** database column, the aggregate property is modelled as a **nullable VO** (`Description?`, `Email?`) and the EF converter is a plain nullable converter (`ValueConverter<Email?, string?>` / `ValueConverter<Description?, string?>`) with **no `ConvertsNulls` override** — EF passes `null` straight through (native `null → null`).

We do **not** use an empty-value sentinel (`Email.Empty`, `Description.Empty`) to stand in for a missing value at the persistence boundary. The `.Empty` members are removed from the value objects; "not set" is represented by `null`, end to end (column → property → read DTO → response).

This convention is shared across every aggregate that has an optional VO: `Project.Description`/`Email`, `Provider.Description`, `ProjectResource.PrimaryInvestigatorEmail`, `Request.ContactEmail`, and `ProjectDomainType.Description`.

## Context

- A non-nullable VO property (`Description` backed by `readonly record struct`) bound to a nullable column **throws `SqlNullValueException` when the column is NULL** — even with a `ConvertsNulls = true` converter that maps NULL → `Empty`. EF materialises the property through a **non-null reader chosen from the CLR property nullability**, so the failure happens *before* the converter runs. Making the property nullable is the only fix that selects the null-tolerant reader.
- The same fault has two surfaces. On the **aggregate read path** (`IRepository.FindAsync`) it throws inside the converter; on the **projection read path** (`IDataQuery.Select(x => x.Vo)`) it throws inside the SQL-to-CLR reader. Both are fixed by the nullable *property*; patching only the converter would leave the projection path broken.
- This was first hit live on `GET /v2/Projects/{shortName}` and the project write paths (commit `1db4e547`, which made `Project.Description`/`Email` nullable and inlined a nullable converter). `Provider.Description` (`GET /v2/Providers/{name}`) and `ProjectResource.PrimaryInvestigatorEmail` (resources read query) carried the identical latent 500.
- Migrations are frozen (the modular core maps an existing schema), so the columns are already nullable in the database; only the CLR model and converters were out of step.

## Considered options

- **Nullable VO + nullable converter, no sentinel (chosen).** One representation of "absent" (`null`) across persistence, read model and API; selects the null-tolerant EF reader; fixes both read surfaces at once.
- **Keep the `.Empty` sentinel + `ConvertsNulls` converter.** Rejected: does not prevent `SqlNullValueException` (the reader is picked from property nullability, not the converter), and forces every read consumer to compare against `== Empty` to re-derive null — a leak that silently mis-maps once a column truly is NULL.
- **Make only the converter nullable, leave the property non-nullable.** Rejected: impossible for the projection path, and inconsistent with the property's contract.
- **Inline a private nullable converter per configuration** (as the original `Project` fix did). Rejected as the standing convention: with four-plus call sites a single shared converter is the DRY, discoverable home; the `Project` inline converter is retrofitted to the shared one.

## Consequences

- `Email.Empty`, `Email.From(string?)` and `Description.Empty` are deleted. Non-persistence callers that used `.Empty` as an in-memory placeholder (e.g. the `SelfOnboardingAccept` error-branch tuple) now use `null`.
- Read DTOs and responses expose `null` for an unset optional VO rather than an empty string; clients must treat the field as nullable.
- New aggregates with an optional VO follow this convention by default: nullable property, shared nullable converter, no sentinel.
