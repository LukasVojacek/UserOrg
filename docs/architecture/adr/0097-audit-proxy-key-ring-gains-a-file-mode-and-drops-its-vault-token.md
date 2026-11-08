# ADR 0097 - The unsealing proxy's key ring gains a file mode and drops its Vault token; the E2E topology finally has an unsealing half

- Date: 2026-08-15
- Status: accepted
- Supersedes / amends: **ADR-0096 §5** - its key ring no longer "lives entirely in Vault", the Vault
  agent joins the list of things that live outside the folder, and the lift-out checklist gains a
  further cross-boundary coupling; and **narrows ADR-0096 §4** - one named exception to "every
  producer-facing hop is HTTPS", for loopback inside a test run. ADR-0096 §1, §2, §3, §6 and §7 are
  unchanged and still binding.
- Relates to: [ADR-0096](0096-audit-log-ships-sealed-to-immudb-through-an-unsealing-proxy.md) (the
  chain this amends), [ADR-0088](0088-audit-log-poc-narrows-adr-0004.md)

## Context

Two things were true after ADR-0096 and are no longer.

The proxy authenticated to Vault with `VaultKeyRing__Token`. It never had to: the Vault agent in
front of it sets `use_auto_auth_token = "force"`, which overwrites any client token with its own, and
every other host in this repository already talks to Vault token-less (`HashiCorpVault.Contracts/VaultOptions.cs`
has no token field). The setting was a credential that changed nothing at runtime and existed only to
be leaked.

And the E2E topology had no unsealing half at all: `TestcontainersTopologyFixture` pointed producers
at a dead URL, so every audit write failed before the spool and took the fail-closed P1 call sites
with it. Giving that topology a real proxy means giving it a key ring, and standing up a Vault agent
inside a Testcontainers fixture buys nothing the test is about.

## Decision

1. **The key ring has two modes, and file mode is not encrypted at rest** (amends ADR-0096 §5).
   `VaultKeyRing__Mode` is required and is either `Vault` or `File`; `Address` + `MountPoint` belong
   to the first, `Directory` to the second, and an `Address` and a `Directory` together stop the host
   at startup - they name two different rings. §5's "its Data Protection key ring lives entirely in
   Vault" now holds only in Vault mode.

   In file mode on Linux - which is what production is - the ring is **plaintext on disk**. That
   inverts the property §5 relied on: today, compromising the key-store volume alone does not open the
   archive, because the private halves on it are wrapped by a ring that is not on it. With a file ring
   on the same host, whoever reads those files can unwrap every sealing key ever issued and open the
   whole audit archive. (On Windows Data Protection DPAPI-encrypts the ring and binds it to the
   user/machine; that is the test host, not the deployment.) The compensating controls are the
   deliberate switch, one `Critical` log record at startup naming the resolved ring directory, and the
   ring directory being narrowed to `0700` on every non-Windows startup, not only when the proxy
   creates it.
   File mode is for topologies whose archive is disposable. Vault mode is the deployment mode.

2. **Vault mode requires a Vault agent in front of the proxy** - a deployment prerequisite, and the
   fifth entry in ADR-0096 §5's list of things that live outside the folder and must be recreated on
   the other side. With the token gone, the proxy's Vault client carries no auth method at all, so the
   only thing that can make its KV calls succeed is an agent supplying the token
   (`use_auto_auth_token = "force"`). This constraint is what the removal introduced, and it has no
   other home.

3. **One exception to ADR-0096 §4: plain HTTP on loopback, between processes of a single test run.**
   In `Tests/E2E/IntegrationTests` the proxy runs as a child process on `127.0.0.1` and the API and
   Gateway children reach it - both `POST /audit` and the key document - over plain HTTP. This is not
   an in-process test server: they are two OS processes talking over a real TCP socket. §4's control
   exists against an on-path attacker who substitutes the key document and turns the audit chain into
   a silent blackhole; between processes of one test on the loopback interface there is no on-path
   position to occupy. The exception is exactly that narrow - tests, loopback, one run - and does not
   reach any deployed topology.

4. **`TestcontainersTopologyFixture` is a further cross-boundary coupling.** ADR-0096's Consequences
   names `Tests/AuditUnsealingProxy.Tests/SealedWireContractTests.cs` as the one thing to re-point when
   the proxy leaves this repository. The fixture is another, and of a different kind:
   `Tests/AuditUnsealingProxy.Tests` travels *with* the proxy, whereas `Tests/E2E/IntegrationTests`
   stays behind and stops working the moment the folder leaves. It couples in two places - a build-only
   `ProjectReference` to `AuditUnsealingProxy.csproj` in `Tests/E2E/IntegrationTests/IntegrationTests.csproj`,
   and the fixture launching `artifacts/bin/AuditUnsealingProxy/<config>/AuditUnsealingProxy.dll` by
   path. Only the first is a reference a tooling search can follow; the second is a string. This is not
   an ADR-0096 §5 violation: the reference points test → proxy, and the proxy still has no
   `ProjectReference` of its own.

   A third inbound reference stays behind too, and it predates this change:
   `src/UserOrg.AppHost/UserOrg.AppHost.csproj:41` - inherited from the MR that introduced the proxy and
   never listed by ADR-0096. It is how the local Aspire topology launches the proxy
   (`builder.AddProject<Projects.AuditUnsealingProxy>`), so lift-out has to replace it with an external
   resource rather than delete it.

5. **The bootstrap guard is one `IXmlRepository` wrapper over whichever ring the mode selected**, and
   it refuses when `GetAllElements()` comes back empty while the sealing key store is not - the state
   in which Data Protection would mint a second ring and make every prior envelope unopenable. It is a
   wrapper rather than a startup check because file mode has no exception path to hang a startup check
   on (`FileSystemXmlRepository` answers an absent directory with an empty list) and because it then
   also sits on Data Protection's periodic refresh.

   **What that continuity is worth, precisely:** on a refresh - a mount that starts answering 404
   mid-life - the guard does prevent the second mint, but `KeyRingProvider` swallows exceptions thrown
   during a refresh, so the process stays up and stays Healthy and the only signal is a log line. Only
   the startup read actually refuses to serve. The guard protects the archive continuously; it
   announces itself only at startup.

## Consequences

- The E2E half of this change is verified by nothing that runs automatically. `IntegrationTests` is
  broken independently on the development machine, and in CI `integration_test` is `when: manual` in
  every rule, `allow_failure: true`, and runs with `--ignore-exit-code '8;10'` - it neither triggers
  on push/MR nor blocks anything. It is proven by review. What runs in every pipeline is the contract
  test in `Tests/AuditUnsealingProxy.Tests` (a real producer sealing against a real file-mode proxy
  host) plus the restart test that reads the ring back from disk; together they cover the runtime
  behaviour the fixture depends on, and neither alone does.
- In file mode the ring directory and `SealingKeyStoreDirectory` share a lifetime by construction: a
  fresh ring beside a populated key store is exactly what decision 5 refuses.
- So does the producer's key-document cache. A warm cache from an earlier run lets a producer satisfy
  `RequireKeyAtStartup` without ever reaching the proxy, then seal to a ring nothing can open - sealing
  succeeds, the spool grows, the proxy answers `UnknownKey` forever, and a naive check ("the endpoint
  is configured and a spool file appeared") passes in exactly that state. Every audit directory in the
  fixture is therefore per-run and deleted on teardown.
- Vault mode has no automated coverage beyond the repository unit tests: that a token-less VaultSharp
  client is accepted through the agent is proven only by a live `aspire run` smoke.
- `src/AuditUnsealingProxy/README.md` is the deployment contract for a folder meant to be lifted out,
  so it changes with this ADR rather than after it: an operator who reads only the README has to reach
  the same configuration.

## Note (2026-09-29): producer key cache lifetime under `aspire run`

The producer key cache and the spool share the lifetime of the proxy's store, as the Consequences
already require of the test fixture. Under `aspire run` both live at
`LocalApplicationData/userorg/<AppHostKey>/<Module>-sealing-keys` and `-spool`, keyed like the store
volume, and the store's owner empties them in the same step that clears the store after Vault came up
without a ring (`src/UserOrg.AppHost/AuditChainExtensions.cs`). The shared `dead-letter` directory is
left for a manual, investigated clear. Moving the dev proxy to file mode was rejected: it changes the
dev topology and the pinned composition test and leaves deployments unchanged. In a deployment a lost
proxy key store still stalls the spool until the producer side of #332 ships. Recovery steps are in
`docs/runbooks/audit-chain-local-recovery.md`. The stall in a deployment is resolved by
[ADR-0116](0116-unknown-sealing-key-is-retry-or-dead-letter-and-the-producer-cache-follows-the-store.md).

## Note (2026-10-09): the dev proxy runs in file mode (#351)

The note above no longer describes the dev topology. Under `aspire run` the proxy now runs with
`VaultKeyRing__Mode=File`, and its key ring sits beside the sealing key store in the volume
`audit-proxy-state-<AppHostKey>`, so a recreated dev Vault takes nothing with it. The owner container no
longer clears the store or the producers' directories. The producers' run-mode directories moved under
`<AppHostKey>/file-ring/`, which starts them empty beside the new ring; the old directories are orphaned
and deleted by hand (`docs/runbooks/audit-chain-local-recovery.md`). The published compose output uses the same
file mode, since its Vault is the same in-memory dev server.
