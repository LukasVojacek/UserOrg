# ADR 0089 - A partial dependency scan fails the job

- Date: 2026-08-13
- Status: accepted

> Numbering note: this decision was first filed as `0094`, a number already held by
> `0094-interactive-provider-admin-identity-and-the-write-asymmetry.md`, and was renumbered to the free
> `0089`. That number previously and briefly held what is now ADR-0093 (see
> [`0093-log-token-id-in-every-component.md`](0093-log-token-id-in-every-component.md), §50-54); it holds
> the dependency-scan decision from 2026-08-13.

## Decision

`dotnet_dependency_scan` treats an **incomplete** scan as a gate failure, not as a pass. Three changes
in `.gitlab/scripts/dotnet-dependency-scan.sh`:

1. **`dotnet list`'s exit code is no longer discarded.** It was `… > "$VULN_JSON" || true` under the
   false comment "`--vulnerable` always exits 0 (it is a report)". It exits **1** whenever it could not
   read some project's `project.assets.json`.
2. **Self-heal, then verify.** A project the `.slnx` drops from the Debug build
   (`<Build Solution="Debug|*" Project="false" />`) is dropped from solution-level *restore* too, so it
   reaches `dotnet list` with no assets file. Restoring that project on its own works, so the script
   retries once. The retry is best-effort; the guard below is authoritative.
3. **A completeness guard** that fails the gate unless all three hold: the JSON parses, `problems[]`
   has no `error` entry, and `projects[]` enumerates exactly as many projects as there are `.csproj`
   in the checkout. Each reason also becomes a `Scan completeness` JUnit failure, so it surfaces in
   the MR widget instead of only in the trace.

   The `.csproj` count comes from `find`, **not** `git ls-files`: the runner clones as one user while
   the job runs as the non-root `app` user, so git rejects the checkout with `fatal: detected dubious
   ownership` and reports zero projects. A first attempt used `git ls-files` and failed the job for
   that reason alone (`enumerated 47 project(s) but 0 .csproj are tracked in git`) while the scan
   itself was complete — a fail-closed guard is only useful if its own inputs are reliable.

`completeness_errors()` is factored out pure (no `jq`, no `dotnet`) and covered by
`.gitlab/scripts/tests/dotnet-dependency-scan.test.sh`, following the `version-preview.sh` idiom.

## Context

The scan enumerates projects by handing the whole solution to
`dotnet list UserOrganization.slnx package --vulnerable --include-transitive`. It was suspected of
**truncating** at `Tests/Core/Infrastructure.DataAccess.Tests` — excluded from the Debug build at
`UserOrganization.slnx:71-73` — and thereby leaving every project ordered after it unaudited.

That premise is wrong, and the correction is the main reason this ADR exists:

- **No cascade.** All 47 projects appear in `projects[]`. What misled the original reading is that
  `--vulnerable` omits the `frameworks` key for *clean* projects, so a complete run legitimately looks
  like a list of bare paths. Only the vulnerable project carries package data.
- **Exactly one project goes unaudited**: `Infrastructure.DataAccess.Tests` is reduced to a path stub
  plus an `error` in `problems[]`. Nothing after it is affected.
- **Nothing was actually being hidden.** A per-project sweep — `dotnet restore` + `dotnet list … --vulnerable`
  over each of the 47 tracked `.csproj`, suppressions deliberately *not* applied — yields exactly one
  finding: `SSH.NET 2025.1.0`, High, transitive, in `Tests/E2E/IntegrationTests` (`net10.0`). Identical
  to what the solution-level run reports. `Infrastructure.DataAccess.Tests` itself has zero
  vulnerabilities. All 47 `.csproj` on disk are in the solution, so no project sat outside the scan.

So the blind spot was never the missing project — it was the **silent-failure machinery** around it.
`|| true` swallowed the non-zero exit, nothing read `problems[]`, and the gate was derived only from
findings extracted out of `projects[]`. Any state that produces no findings therefore reads as clean.
Reproduced against the pre-change script: when `dotnet list` emits nothing at all, it printed
`OK: No vulnerable NuGet packages detected.` and exited **0**. That is the false pass — a scan that
audited nothing reporting success. Under `set -euo pipefail` the same input now exits 1 naming the
reason.

Pipeline #34440 job 90587 (the trace cited as the failing run) is *not* an instance of this bug: it
restored 46 of 47 projects, hit the SSH.NET advisory, and failed correctly on it. Its exit 1 comes from
the real finding, which is `T260812f`'s subject, not from the truncation.

## Consequences

- A scan that cannot audit every project fails loudly, with the project paths and the reason. The
  gate can no longer report success on a partial audit.
- `Infrastructure.DataAccess.Tests` is now genuinely audited, via the retry, while it stays excluded
  from the Debug build.
- The separate task that re-enables `Infrastructure.DataAccess.Tests` in the solution makes the retry
  a no-op — solution restore will cover the project and `problems[]` will be empty — but harmless, and
  the guard keeps its value for the *next* excluded project. Neither task blocks the other.
- Adding a `.csproj` without adding it to `UserOrganization.slnx` now fails this job, because the
  count check no longer matches. That is intentional: such a project is otherwise never scanned at all.
- The guard is fail-closed: if the `.csproj` count cannot be established it reads 0, the counts
  diverge, and the job fails rather than passing on an unverifiable enumeration. The `find` exclusions
  (`artifacts/`, `.claude/`) are the UseArtifactsOutput tree and nested worktrees; a project directory
  added at either path would be miscounted.
- Known at the time and left alone: the `NuGetAuditSuppress` extraction relied on
  `Directory.Packages.props` holding at least one entry — `grep -oP` exits 1 on none, and `pipefail`
  kills the job. It failed loudly with no `OK`, so it was never a false pass of the kind this ADR is
  about. That reliance has since fired for real: removing the last suppressed advisory took the job
  down right after the scan banner, before the gate. It is fixed — `suppressed_advisory_urls()`
  guards the `grep` with `|| true`, so zero entries yield an empty suppression list, not a dead job.

## Rejected alternatives

- **Removing the `.slnx` build exclusion.** Owned by a separate task, and it would fix only today's
  instance while leaving the silent-pass path in place for the next one.
- **Restoring every tracked `.csproj` up front** instead of retrying the ones that failed. 47 sequential
  restores on a cold CI cache, to work around a condition that normally affects one project.
- **Scanning project-by-project instead of by solution.** 47 `dotnet list` invocations replacing one,
  for no additional coverage — the sweep proved the solution-level enumeration already reaches every
  project. The gap was the unread `problems[]`, not the enumeration.
