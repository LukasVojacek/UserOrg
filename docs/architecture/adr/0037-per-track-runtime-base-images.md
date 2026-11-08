# Per-track runtime base images + preview debug base

App images are no longer built on the SDK image. Each release track layers the app onto a runtime
base chosen for that track, via `publish-images.sh` (`-p:ContainerBaseImage=$APP_BASE_IMAGE`):

- **preview** image (manual, feature/MR) → **`dotnet-preview-base`**
- **rc** image (auto, every `main` merge) → **`dotnet-prod-base`**
- **primary release** image (manual `RELEASE=true`) → **`dotnet-prod-base`**

`dotnet-preview-base` is built `FROM dotnet-prod-base` and adds a debug layer: the Microsoft remote
debugger `vsdbg`, the .NET diagnostics CLIs (`dotnet-trace/dump/counters/gcdump`), and network
tooling (`telnet/dig/ping/netcat/tcpdump/lsof/strace`). All four bases are built by manual,
main-only jobs (`build_*_base`).

## Why

Previously every app image used `ContainerBaseImage=$DOTNET_IMAGE` (the SDK dev base), so even the
production release shipped the compiler, NuGet, and global tools — large and a needless attack
surface. Splitting by track makes release/rc images the minimal `aspnet` runtime, while keeping full
debuggability where it is actually used: the preview image you spin up to investigate something.

The preview base is built **FROM the prod base** rather than as an independent lineage so a preview
container is byte-identical to production *plus* a debug layer — "debug what you ship", with no
second base that can drift from prod.

## Trade-offs taken

- **Diagnostics tools come from an SDK stage.** The prod base is `aspnet` (runtime only, no
  `dotnet tool`), so the diagnostics CLIs are installed in an `sdk` build stage with `--tool-path`
  and `COPY`ied in (framework-dependent, run on the inherited runtime). Microsoft's container
  diagnostics guidance endorses exactly this multi-stage copy.
- **Debug only on preview, not rc.** rc images use the minimal prod base. If an rc deployment ever
  needs a live debugger, switch its `APP_BASE_IMAGE` to `dotnet-preview-base` — the wiring is a
  single `export` per job in `.docker_build_stage.yml`.
- **Separate build jobs, `:latest` pull.** `build_dotnet_preview_base` builds `FROM`
  `dotnet-prod-base:latest` (`--build-arg PROD_BASE`, `--pull`) instead of chaining `needs:`. Simple
  and matches the one-job-per-image layout, at the cost of one ordering rule: **rebuild
  `dotnet-prod-base` before `dotnet-preview-base`** when the prod base changes, or the preview base
  layers onto a stale prod base.

## Consequences

- rc and release containers no longer contain the .NET SDK/CLI, `jq`, or `telnet`; an entrypoint
  must not shell out to SDK commands. `chromium` is retained in the prod base for the app's
  headless-browser needs.
- Attaching a debugger to a preview deployment requires the **`SYS_PTRACE`** capability at deploy
  time (`vsdbg` and `dotnet-dump` both need `ptrace`); `vsdbg` lives at `/vsdbg/vsdbg`. This is a
  runtime setting the image cannot satisfy itself — see `docs/ci-cutover.md`.
- The base-image pull from the private `$CI_REGISTRY` reuses the same
  `DOTNET_CONTAINER_REGISTRY_UNAME/PWORD` as the push, because the SDK applies one credential set to
  both source and destination registries and they are the same registry.
- **Runs as root.** The bases end on `USER root`, overriding the aspnet base's non-root
  `app`/`$APP_UID` default; child images produced by `PublishContainer` inherit it. The target
  runtime does not enforce `runAsNonRoot`, and the app requires root. They still listen on **8080**
  (the aspnet default `ASPNETCORE_HTTP_PORTS`): deployments already target 8080, so the port is
  left unchanged — root could bind <1024 but nothing needs it. Deployments must point Service
  `targetPort` and probes at **8080**. (Earlier revisions ran non-root as UID 1654 for
  `runAsNonRoot: true`; that constraint no longer applies.)

Status: accepted
