# Single-`main` trunk-based CI model

We replaced the `develop → testing → main` branch-promotion model with a single trunk: work
happens on `feat|fix|hotfix/*` branches, merges to `main` via MR, and `main` is the only
long-lived branch (`$CI_DEFAULT_BRANCH`). The pipeline (modeled on the LEXIS `bff` project) is
branch-name-agnostic — all `rules:` key off `$CI_DEFAULT_BRANCH` and the `feat|fix|hotfix` regex
instead of the old hard-coded `BRANCH_DEVELOP/TEST/RELEASE` variables.

## Why

Three branches each mapped to an environment and accumulated divergent release/promote history
(at cutover `main` was 60 commits ahead of `develop`, `develop` 4 ahead of `main`), making
back-merges and "which branch is truth" a recurring tax. A single trunk removes the promotion
choreography; environments are fed by image tags (rc per merge, X.Y.Z on release) rather than by
branches.

## Consequences

- The `feat|fix|hotfix/*` convention is **enforced**: a push to a branch not matching the regex
  gets no branch pipeline (it still gets an MR pipeline once an MR is opened). Existing
  issue-number branches (`216-…`, `103/…`) must be renamed to the convention to get WIP pipelines.
- A duplicate-pipeline guard in `workflow:` suppresses the branch pipeline when an MR is open.
- The GitLab default branch, branch protections, schedules and the fate of `develop`/`testing`
  are out-of-repo cutover steps — see [docs/ci-cutover.md](../../ci-cutover.md).

Status: accepted
