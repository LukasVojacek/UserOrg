# ADR-0053: Enforce conventional-commit MR titles via a first-stage CI lint gate

- Status: Proposed
- Date: 2026-07-01
- Deciders: Lukáš Drábek

## Context

The project uses semantic-release ([`.releaserc.js`](../../../.releaserc.js)) with the
`conventionalcommits` preset, and MRs merge with `merge_method: ff` +
`squash_option: always`. The **MR title becomes the single squash commit on `main`** and
is the string semantic-release parses to compute the next version.

Nothing enforced that title's format. A merge landed on `main` as
`0a768e4f "HEAppE command template sync — per-location partial results"` — a
non-conventional subject that semantic-release silently ignored (no version bump). It had
to be reworded and force-pushed after the fact ([ADR-0007](0007-tagless-rc-track-manual-primary-release.md)
context). We want to prevent this at the source instead of fixing `main` history later.

GitLab CE has no native commit-message format enforcement (Push Rules are Premium/EE), so
the gate must be a CI job. `only_allow_merge_if_pipeline_succeeds` is already `true`, so a
failing job blocks merge.

## Decision

Add a **blocking CI job that lints the MR title** against conventional-commits, running as
the pipeline's first stage.

- **Target = MR title only** (`$CI_MERGE_REQUEST_TITLE`). Branch commits are squashed away,
  so linting them is noise; the title is what reaches `main`.
- **Tool = commitlint** (`@commitlint/config-conventional`) via a committed
  `commitlint.config.js`, run in the existing Node image (`NODE_JS_IMAGE`).
- **Config aligned to `.releaserc.js`**, not stock. Overrides:
  - `type-enum` includes the repo's non-default types `dep` and `other`, excludes `style`.
  - `subject-case` off — titles contain proper nouns (e.g. "HEAppE").
  - Types are enforced **lowercase** (conventional-commits standard). This is *stricter* than
    `.releaserc.js`, which also tolerates `Feat`/`FEAT`; enforcing lowercase upstream is safe
    because any lint-clean title is a subset of what the release config accepts. Listing all
    case variants (as `.releaserc.js` does) was rejected as noise.
- **First stage, `needs: []`** — fastest feedback, fails before build minutes are spent.
- **Merge enforcement** relies on the existing `only_allow_merge_if_pipeline_succeeds`.

## Consequences

- Malformed MR titles fail CI and cannot be merged; `main` subjects stay parseable and
  version bumps become deterministic.
- The lint rules and `.releaserc.js` type list are now **two sources that must stay in
  sync**. If a release type changes, update `commitlint.config.js`'s `type-enum` too.
- Runtime `npx` fetches commitlint per run (network dependency, small latency). If that
  becomes a problem, bake commitlint into the semantic-release base image.
- Only MR-event pipelines run the job; direct pushes to `main` skip it (main already
  received the validated title as its commit).
