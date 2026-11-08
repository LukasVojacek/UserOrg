# ADR 0012 - Provider and Location names are case-insensitive natural keys

- Date: 2026-06-09
- Status: accepted

## Decision

`ProviderName` and `LocationName` value objects are **case-insensitive natural keys**. Their identity — `Equals` and `GetHashCode` — compares the value with `StringComparer.OrdinalIgnoreCase`. The `Create`/`From` factories only **trim** surrounding whitespace; they **preserve the original casing** in `Value`.

This gives one definition of equality everywhere the key is used (routing, repository lookup, uniqueness checks, in-memory aggregate matching, and claims-based write authorization) while keeping the stored/displayed casing intact. EF Core materializes both VOs through `From` (`HasConversion(v => v.Value, value => ProviderName.From(value))`), so GET endpoints return the name **as stored** — no read-path behavior change.

## Context

- The provider/location write endpoints are **name-based** (route, repository key, and — for providers — the authorization claim all carry the natural name). Authorization for a provider service-key compares the route name against a `providerName` claim by value equality (ADR-0011).
- Both VOs are `readonly record struct`s. The compiler-generated equality is **ordinal, case-sensitive**, and the factories originally only `Trim()`. This produced inconsistent equality semantics across layers:
  - **Provider:** SQL Server's default collation is case-**insensitive**, so `FindAsync("acmehpc")` loads stored `"AcmeHPC"` and uniqueness pre-checks match case-insensitively — but the Core claims-only ownership comparison was case-**sensitive**. A legitimate owner calling `PUT Providers/acmehpc` while the claim carries `"AcmeHPC"` would be wrongly rejected with `403`.
  - **Location:** matching is done purely **in-memory** on the loaded aggregate (`_providerLocations.Any(l => l.Name == name)`) with **no DB collation safety net**, so `"GPU"` and `"gpu"` were treated as distinct — a `gpu` update 404s against a stored `GPU`, and duplicates slip past `AddLocation`.
- A natural key must have **one** definition of equality end to end. The fix belongs in the value object so the invariant lives in the type rather than being re-derived (and potentially forgotten) at every call site.

## Considered options

- **Case-insensitive equality, preserved casing (chosen).** Override `Equals`/`GetHashCode` with `OrdinalIgnoreCase`; `Create`/`From` only trim. Identity is uniform end to end, and `Value` keeps the original casing, so reads are unchanged and no schema column is needed. Cost: equality intentionally ignores casing in `Value`, which is mildly surprising for a record struct — callers must compare via the VO, never by `Value` string-equality.
- **Canonicalize the value (lowercase in `From`/`Create`).** Also gives one definition of the key, but because EF materializes through `From` it rewrites the **read** representation: GET output casing changes for existing rows — a wire-visible, hard-to-reverse change — purely as a side effect of fixing equality. Rejected: it sacrifices display fidelity for no extra benefit over case-insensitive equality.
- **Case-insensitive comparison at the authorization helper only.** Surgical, no read impact — but fixes only the provider `403`, not the location in-memory 404/duplicate gap, and leaves two different case strategies across the two VOs. Rejected for inconsistency.

## Consequences

- **No read-path change and no migration.** GET endpoints return names exactly as stored; existing rows are untouched. The casing lives in the existing `nvarchar(100)` column — no schema change (consistent with frozen migrations).
- **Equality is uniform.** Provider authorization (ADR-0011) compares VOs, so ownership is case-insensitive without special-casing; location in-memory matching and duplicate detection are case-insensitive too.
- **Uniqueness semantics tighten for locations.** `"GPU"` and `"gpu"` can no longer coexist as active locations under one provider; `AddLocation` rejects the second as a duplicate (`409`).
- **Compare via the value object, not its `Value`.** Because `Equals`/`GetHashCode` ignore casing while `Value` preserves it, code must compare `ProviderName`/`LocationName` instances (or use them as dictionary/set keys) and must not fall back to `name.Value == otherString` ordinal comparison.
