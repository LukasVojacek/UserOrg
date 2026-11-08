# Architecture Decision Records

This directory holds **all** ADRs — both **architecture** decisions (module boundaries,
contracts, persistence, security, API standards) and the **behavioural / runtime**
decisions (e.g. HEAppE fast-fail semantics, degraded-state handling) that once lived in a
separate `docs/adr/` tree.

## One tree, globally unique numbers

There is a single ADR tree with a single, globally unique numbering sequence, so a bare
`ADR-NNNN` citation — the form used directly in code comments (e.g. "ADR 0073 / ADR 0074")
— is unambiguous.

The former `docs/adr/` behavioural tree was merged in here as `0070`–`0084` (issue #267);
the two trees previously numbered independently, which made the same bare number resolve to
two different files. If you find a stale reference to `docs/adr/`, it now points here.

> History note: the `feat/refactor-core` ⇄ `origin/main` merge
> ([ADR 0033](0033-merge-main-adopt-waldur-efp-layout.md)) briefly produced duplicate
> numbers *within this tree*. They were resolved by renumbering the incoming files to
> `0035`–`0038` ([ADR 0034](0034-heappe-connection-canonical-port-and-portlater-closure.md)).
