# Architecture.Tests

Executable architecture rules. Until now the hexagonal and modular boundaries in this repo were
enforced by review alone — nothing failed when they were violated. These tests do.

The project is listed in `UserOrganization.slnx`, so CI's `unit_tests` job
(`dotnet test --solution UserOrganization.slnx`) runs it like any other suite: **informational on a
feature branch or MR** (`allow_failure: true`), **blocking on `main`**. A violation therefore shows
up as a red test in the MR without gating the merge, and hard-stops the release pipeline if it
reaches `main`.

## What is asserted

Every rule here is **green on the code as it stands**. A rule is only committed once it has been
verified against reality; the repo's own `docs/adr/` and `CONTEXT.md` win over any generic
architecture standard. Notably, this codebase is **modular**: a module owns its own `DbContext`
and its own migrations (`Heappe.Persistence`, `Forge.Core.Persistence`). There is no single
central persistence assembly, and no rule here pretends otherwise.

**`DependencyDirectionTests`** — who may know that whom exists:

- `UserOrg.Shared` is the shared kernel and stays a leaf.
- Core `Domain` does not reach outward to `Application`, `Infrastructure.DataAccess`, or a host.
- Core `Application` does not know its persistence adapter or any host.
- No module references a host or composition root (`API`, `UserOrg.AppHost`, `UserOrg.Gateway`,
  `Userorg.Adapter`).
- No module references Core.
- The integration families (Heappe / HashiCorpVault / Waldur) are mutually invisible.
- A `*.Contracts` assembly never references its own implementation.
- Core `Application` reaches the Heappe module only through `Heappe.Contracts`.

**`PersistenceIgnoranceTests`** — entities do not know how they are stored:

- Core `Domain` types depend on neither EF Core nor `DataAnnotations`.
- `Heappe.Persistence.Entities` types depend on neither EF Core nor `DataAnnotations`.
- A module does not expose its `DbContext` publicly.
- A module's `IEntityTypeConfiguration<T>` only ever maps entities that module owns.

## How the rules are expressed

Boundary rules read `Assembly.GetReferencedAssemblies()` rather than parsing `.csproj` files: it
is what the compiler actually emitted, so a declared-but-unused reference cannot skew a result.
All boundary rules are negative ("must not reference"), which stays sound under the compiler's
pruning of unused references — pruning can remove a name, never invent one.

Two consequences worth knowing before you trust a green run:

- A forbidden `ProjectReference` that is declared but **never used** is pruned by the compiler, so
  these rules do not see it. This is deliberate — an unused reference is not yet coupling — but it
  means the boundary is caught at first *use*, not at the moment the `.csproj` edit lands.
- Coupling created purely by string-based reflection (`Assembly.Load("Domain")`) emits no assembly
  reference and is invisible here. Nothing in this repo does that today.

Type-level rules use [NetArchTest.Rules](https://github.com/BenMorris/NetArchTest), chosen because
it reads IL through Mono.Cecil (so a rule cannot be fooled by a `using` that is only cosmetic) and
needs no runner of its own — it is a plain assertion inside the repo's existing xUnit v3 setup.

Rules that could pass vacuously (an empty type set after a rename) carry an explicit `Assert.NotEmpty`
guard.

## Running

This repo uses xUnit v3 on Microsoft.Testing.Platform with `UseArtifactsOutput`, so
`dotnet test --filter` runs **zero** tests. Run the built executable instead:

```sh
dotnet build Tests/Architecture/Architecture.Tests/Architecture.Tests.csproj
./artifacts/bin/Architecture.Tests/debug/Architecture.Tests.exe
./artifacts/bin/Architecture.Tests/debug/Architecture.Tests.exe --filter-class "*DependencyDirectionTests"
```

## Adding a rule

1. Verify it against the current code first. **A rule you cannot verify is a rule you do not write.**
2. If existing code violates a rule you believe in, do **not** commit a weakened version and do not
   quietly scope it to new code. Raise the violation and let the team decide.
3. Add the assembly under test as a `ProjectReference` only if the rule inspects *its* types or
   references. Hosts are named as strings precisely so this project never references them.
