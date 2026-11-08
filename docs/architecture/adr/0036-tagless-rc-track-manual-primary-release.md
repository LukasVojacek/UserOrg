# Tag-less rc track + manual primary release

Releasing is split into two tracks on `main`:

- **rc track (automatic, every merge):** `version-preview` runs `semantic-release --dry-run` to
  project the next primary version, and `publish_rc_images` builds the API + Gateway images tagged
  `<next>-rc.<count>` (`count` = commits since the last primary tag, so it resets each release). It
  creates **no git tag, no GitLab Release, no promote**.
- **primary release (manual):** a "Run pipeline" on `main` with `RELEASE=true` runs the full
  `semantic-release` (`release_version`), which creates the `X.Y.Z` git tag + GitLab Release, then
  `publish_primary_images` tags `X.Y.Z` and `promote_*` copies both images to the EFP registry.

## Why

A trunk-based model needs "every merge is potentially shippable" without "every merge is a
release". The dry-run rc track gives a per-merge installable image for dev/test environments while
keeping releases a deliberate, human-gated act. Promotion to the production EuroHPC EFP registry
happens only on the manual release, never automatically on merge.

## The invariant that makes it correct

semantic-release computes the version and release notes from *all commits since the last git tag*.
Because **only the manual primary release ever creates a tag** — the rc track and the manual
feature preview are strictly `--dry-run` — "since the last tag" equals "since the last manual
release". The GitLab Release therefore always covers the whole accumulated range, with no extra
bookkeeping.

If anyone ever makes the rc/preview track create a tag (e.g. "to version the dev images
properly"), it will silently truncate the next release's notes to only the commits after that
tag. **The rc/preview track must remain tag-less.**

## Consequences

- `.releaserc.js` loads `@semantic-release/gitlab` only when `RELEASE === 'true'`, so the dry-run
  needs no token and publishes nothing.
- No `CHANGELOG.md` is committed back to `main`; GitLab Release notes are the sole changelog
  (supersedes the previous `@semantic-release/changelog` + `@semantic-release/git` setup).
- `GIT_DEPTH: 0` is required on both `version-preview` and `release_version` so the full
  since-last-tag range is visible.

Status: accepted
