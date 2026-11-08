# AGENTS.md

UserOrg: .NET 10 Aspire solution (`UserOrganization.slnx`) for the LEXIS platform. Core REST API,
YARP gateway, SQL Server + EF Core, immudb audit log, HashiCorp Vault. The forge is GitLab at
opencode.it4i.eu (`lexis-platform/backend/userorg`); the `.github/` folder is GitHub-only tooling and
does not apply here.

## Where code goes

- New and changed features belong in the modular core `src/Modules/Core/`
  (`Core` → `Core.DataAccess` → `Core.Domain`, one folder per use case under
  `Features/<Aggregate>/<UseCase>/`). `src/Core/` is the legacy monolith being migrated out of;
  change it only to fix what still lives there.
- Before writing an endpoint, aggregate, domain error, read query or test, read
  `docs/architecture/module-conventions.md`: it holds the required shapes (C# 14 extension-member
  endpoints, `Lukdrasil.Error` results, SQLite-over-the-real-model tests, the LINQ syntax rule).
- Domain vocabulary is `CONTEXT.md`. A design decision becomes a new ADR in `docs/architecture/adr/`
  plus a line in `docs/architecture/index.md`.

## Build and test

```sh
dotnet build UserOrganization.slnx                    # analyzers and code style fail the build
dotnet format UserOrganization.slnx --verify-no-changes
dotnet test --solution UserOrganization.slnx          # Microsoft.Testing.Platform, not VSTest
dotnet test --project Tests/<path>/<Project>.csproj --filter-query "/*/*/<Class>/<Method>"
dotnet test --project Tests/E2E/IntegrationTests/IntegrationTests.csproj -p:RunContainerTests=true
```

- The E2E project runs only with `-p:RunContainerTests=true` (Testcontainers, needs Docker) and only
  locally. Leave the manual CI `integration_test` job alone.
- A build regenerates the `API_*.json` OpenAPI documents at the repo root. Commit them together with
  the endpoint change that altered them; a diff with no endpoint change is noise to revert.
- Local stack: `aspire run` from the repo root. Stop containers left over from a previous run first.

## Git and GitLab

- Branch off `main` as `feat/…`, `fix/…` or `hotfix/…`; `support/vX.Y.x` branches carry hotfixes for
  older releases.
- MR into `main`, squash merge. The MR title is the commit that lands and commitlint checks it:
  conventional commits with the types in `commitlint.config.js`. The MR template needs at least one
  `#issue` reference. Commits and MR descriptions carry no AI attribution trailer.
- Every merge to `main` publishes an rc image; a tagged release is a manual pipeline on `main` with
  `RELEASE=true`.
- Issue state lives in the stage labels (`1. Classification` … `7. Done`), not in open/closed. An
  issue an agent drafts gets `ai-drafted`; a defect an agent found gets `ai-found-bug`.
- `glab` is logged in and fine for reads. When a write (issue, note, MR) answers 401, call the REST
  API with curl and the token from `glab config get token --host opencode.it4i.eu`.

Code, comments, commits and docs are written in English.
