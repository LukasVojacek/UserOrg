# CI cutover: `develop → testing → main` ➜ single `main`

This runbook covers the steps that live **outside** the repository (GitLab settings + git history)
to complete the migration to the single-`main` trunk model. The in-repo pipeline changes ship in
the MR that adds this file. See [ADR-0035](architecture/adr/0035-single-main-trunk-model.md) and
[ADR-0036](architecture/adr/0036-tagless-rc-track-manual-primary-release.md) for the rationale.

## Order of operations

1. **Reconcile branches.** Ensure `main` contains everything from `develop` (and `testing`).
   At analysis time `develop` was 4 commits ahead of `main`. Bring those onto `main`:
   ```
   git checkout main && git pull
   git merge --no-ff develop
   git push origin main
   ```
   Verify nothing is left behind: `git log --oneline main..develop` and `…main..testing` are empty.

2. **Merge this pipeline MR into `main`** (branch `feat/single-main-pipeline`).

3. **Switch the GitLab default branch** to `main`:
   Settings → Repository → Branch defaults → default branch = `main`.

4. **Branch protection.** Protect `main` (Settings → Repository → Protected branches): no direct
   push, merge via MR only. Remove protection on `develop`/`testing`.

5. **`RELEASE` variable.** No action needed — it is declared in `.gitlab-ci.yml` as a prefilled
   dropdown on the "Run pipeline" form. Confirm `EFP_GITLAB_TOKEN` (used by `promote_*`) and the
   secret-detection/release tokens are still present under Settings → CI/CD → Variables.

6. **Nightly schedule (security posture).** Settings → CI/CD → Schedules → New schedule:
   - Target branch: `main`
   - Cron: e.g. `0 2 * * *`
   Only `secret_detection` and `dotnet_dependency_scan` run on a schedule pipeline (continuous CVE
   + secret posture on `main` HEAD); the build-dependent quality jobs do not.

7. **Open MRs.** Retarget any MR currently pointing at `develop` to `main`.

8. **Retire `develop` / `testing`.** Once steps 1 & 7 confirm `main` holds everything and no MR
   targets them, delete both branches (or set read-only first if you want a grace period).

## How the new model behaves (quick reference)

| Trigger | What runs |
|---|---|
| Push to `feat\|fix\|hotfix/*` (no MR) | build, check, security (feature jobs), tests (non-blocking); preview image is a manual button |
| Merge request | same as above (branch pipeline suppressed while MR is open) |
| Merge to `main` | build, tests (**blocking**), `version-preview`; `publish_rc_images` → `<next>-rc.<count>` **only when image inputs changed** (`src/**`, `roleManagmentRBAC.json`, build props) — cosmetic merges build no image (no tag, no promote) |
| Nightly schedule (on `main`) | `secret_detection`, `dotnet_dependency_scan` |
| "Run pipeline" on `main`, `RELEASE=true` | `release_version` (git tag `X.Y.Z` + GitLab Release), `publish_primary_images` → `X.Y.Z`, `promote_*` → EFP |
| Manual on `main` push | base-image rebuilds (`build_*_base`) |

## OpenAPI documents

The API project generates its OpenAPI documents at build time
(`Microsoft.Extensions.ApiDescription.Server`): one file per `AddOpenApi` document, emitted to the
**repository root** (`OpenApiDocumentsDirectory` = `../../..` relative to the project, per MS docs) —
`API_user.json` (33 paths), `API_administration.json` (16 paths) and `API_service.json` (18 paths, the
ungated legacy endpoints). They are committed, so both
`lint_openapi` and `openapi_diff` work against them. The files are **OpenAPI 3.1**, matching the
runtime document served via `MapOpenApi`: Spectral gained 3.1 support at v6.0.0, the repo pins
v6.15.1, and the pinned CLI run against the real 3.1 output reports findings identical to 3.0.

`lint_openapi` currently reports real findings (missing descriptions, an array without `items`,
unused components); the job is `allow_failure`, so it surfaces them without blocking — tighten the
spec or the ruleset over time.

> Because the specs are committed, a build regenerates them in the working tree. Regenerate and
> commit them alongside API changes (`dotnet build src/Core/API`) so they stay in sync.

## Container base images

Four base images are built by manual, main-only jobs in `.gitlab/stages/.base_images_stage.yml`
(`build_*_base`, each pushing `:<sha>` + `:latest`). Two are CI tooling; two are app **runtime**
bases that `dotnet publish /t:PublishContainer` layers the app onto (`publish-images.sh` →
`-p:ContainerBaseImage=$APP_BASE_IMAGE`):

| Image | Built from | Used as | Contains |
|---|---|---|---|
| `dotnet-dev-base` | `sdk:10` | `image:` for all CI jobs (build/test/security/release/docker) | SDK + dotnet diag/global tools + `jq` + debug utils + Seeds |
| `semantic-release-base` | `node:24` | release/version jobs | semantic-release + plugins |
| `dotnet-prod-base` | `aspnet:10` | base of **rc** + **primary release** app images | minimal runtime + curl + chromium + Seeds |
| `dotnet-preview-base` | `dotnet-prod-base` | base of the manual **preview** app image | prod base **+ vsdbg + dotnet-trace/dump/counters/gcdump + telnet/dig/ping/netcat/tcpdump/lsof/strace** |

**Per-track wiring** (set via `APP_BASE_IMAGE` in each docker-stage job's `before_script`):
preview image → `dotnet-preview-base`; rc + primary release images → `dotnet-prod-base`.

**Rebuild order.** `dotnet-preview-base` is built `FROM dotnet-prod-base:latest` (passed as
`--build-arg PROD_BASE`, `--pull`ed fresh). If you change `Dockerfile.dotnet-prod-base`, rebuild
`build_dotnet_prod_base` **before** `build_dotnet_preview_base`, or the preview base layers debug
tooling onto a stale prod base.

**Runs as root.** Runtime bases end on `USER root` (target runtime does not enforce
`runAsNonRoot`; the app requires root). They still listen on **8080** (aspnet default), which
deployments already target — so the port is unchanged. When rolling out images built on a rebuilt
base, keep k8s Service `targetPort` and probe ports at 8080.

**Attaching a debugger to a preview deployment.** `vsdbg` lives at `/vsdbg/vsdbg`; point your
`launch.json` `debuggerPath` there. `vsdbg` and `dotnet-dump` both need the **`SYS_PTRACE`**
capability — deploy the preview container with `--cap-add=SYS_PTRACE` (Docker) or
`securityContext.capabilities.add: ["SYS_PTRACE"]` (Kubernetes), or attach/`dotnet-dump` silently
fails.

## Known follow-ups (not blockers)

- _None outstanding._ `dotnet_dependency_scan` parses its JSON with `jq` (already in
  `Dockerfile.dotnet-dev-base`); it needs no `python3` and no per-job package install — the CI
  runner is non-root and cannot `apt-get`.
