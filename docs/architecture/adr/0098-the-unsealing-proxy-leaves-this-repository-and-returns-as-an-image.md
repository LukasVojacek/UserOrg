# ADR 0098 - The unsealing proxy leaves this repository and returns as a versioned image

- Date: 2026-08-17
- Status: accepted
- Supersedes / amends: **ADR-0096 §7** and **ADR-0097 decision 6** - the lift-out they describe as
  future has happened, so the checklists they carry are now history rather than work. Amends
  **ADR-0096 §4** for local development the way ADR-0097 amended it for a test run. Everything else in
  both ADRs is unchanged and still binding: the seal format, the envelope, the append-only archive and
  the obligation not to lose a sealing key are properties of the chain, not of where the code lives.
- Relates to: [ADR-0096](0096-audit-log-ships-sealed-to-immudb-through-an-unsealing-proxy.md) (the
  chain), [ADR-0097](0097-audit-proxy-key-ring-gains-a-file-mode-and-drops-its-vault-token.md) (the
  key ring modes this consumes)

## Context

ADR-0096 §7 wrote the proxy as a folder meant to be lifted out, and ADR-0097 decision 6 counted the
three inbound references that lift-out would cost. It now lives at
`opencode.it4i.eu/lexis-platform/backend/credential-service/audit/auditproxy/auditunsealingproxy`, with its own
pipeline and its own tests, and publishes one artifact: a container image.

No NuGet package came with it, because nothing here compiles against it. Every runtime hop is HTTP and
neither call is made by code in this repository - `POST /audit` by the drain worker in
`Lukdrasil.StepUpLogging`, `GET /.well-known/ogham-sealing-keys.json` by `Ogham.Sealing`. The shared
contract that matters already ships as a package, and it is Ogham: it holds the seal format on both
ends. Beyond it the two halves agree on one JSON envelope, and the proxy forwards its `sqlExec` member
without reading a single `audit_log` column.

The three inbound references ADR-0097 counted were of two kinds. The AppHost's
`AddProject<Projects.AuditUnsealingProxy>` and `IntegrationTests`' build-only `ProjectReference` both
existed to get a binary to run. `InternalsVisibleTo AuditUnsealingProxy.Tests` on
`UserOrg.ServiceDefaults` was not a runtime coupling at all - it let the proxy's own contract test
seal with the real `AuditSqlSealer`.

## Decision

1. **`src/AuditUnsealingProxy/` and `Tests/AuditUnsealingProxy.Tests/` are deleted here.** The tests
   travel with the code they test. The producer-side tests do not move: `AuditSqlSealerTests` and
   `ImmuDbCredentialsTests` stay in `Tests/Core/ServiceDefaults.Tests/Audit/`, because the columns they
   assert on belong to this repository.

2. **Both topologies consume the published image, pinned to a semantic version.** The AppHost's
   `AddProject` becomes `AddContainer` (`AuditChainExtensions.ProxyImage` / `ProxyTag`), and
   `TestcontainersTopologyFixture` starts the same image through `ContainerBuilder` instead of launching
   `artifacts/bin/AuditUnsealingProxy/<config>/AuditUnsealingProxy.dll`. Never a moving tag: the archive
   is append-only, so which build unsealed a given record has to stay answerable after the fact, and
   `latest` cannot answer it. The tag is written in two places, one per topology, and they are kept in
   step by hand.

3. **Locally the producer-facing hop is plain HTTP** (amends ADR-0096 §4, as ADR-0097 did for a test
   run). §4 wants the transport standing between an on-path attacker and the key document, because
   producers are not authenticated. Under `aspire run` and in the E2E fixture every producer reaches the
   proxy over loopback, where there is no on-path attacker to stand against. Terminating TLS in the
   container instead would mean a certificate inside an image this repository does not build. A deployed
   topology still owes the chain HTTPS, and owes it from outside the image.

4. **The two topologies keep the proxy's directories differently, and deliberately.** The AppHost
   bind-mounts the sealing key store from `externalServices/auditVolume/audit-proxy-sealing-keys`,
   because it holds the private half of every key ever issued and must outlive the container. The
   fixture keeps both the ring and the key store inside the container, because ADR-0097's consequence
   holds: they share a lifetime, and a fresh container per run gives that with no mount to keep in step.
   Every path handed to the image is absolute - a relative one resolves against the container's working
   directory, and a healthy proxy then writes into a layer that dies with it.

## Consequences

- **That `AuditSqlSealer` emits exactly the envelope the proxy accepts is now covered by no unit test
  in either repository.** Its own columns are still tested here; the extracted repository's contract
  test seals a locally-declared envelope of the same shape. Nothing ties the two together automatically.
  Only an end-to-end run against the published image does, and the only topology that could is
  `IntegrationTests`, which ADR-0097 already recorded as proven by review rather than by CI.

- Pulling the image needs a `docker login` against `opencode.it4i.eu:5050` on any machine that runs
  `aspire run` or the E2E suite. A machine without it fails at container start, not at build.

- The image runs as a non-root user. Docker Desktop's file sharing ignores ownership, so the AppHost's
  bind mount works there; on a native Linux daemon the mounted directory has to be writable by that
  user or the proxy will not start.

- Upgrading the proxy is now a two-repository operation: release there, then bump the tag here in both
  places. That is the cost decision 2 buys - a build that can be named after the fact.
