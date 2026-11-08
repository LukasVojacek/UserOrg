#!/usr/bin/env bash
set -euo pipefail

# Projected next PRIMARY version via a semantic-release dry-run — NO git tag, NO changelog,
# NO GitLab Release. Writes PRIMARY_NEXT + RC_COUNT to preview.env for the rc/preview image jobs
# (main rc -> -rc.<RC_COUNT>, feature preview -> -preview.<pipeline-iid>).
#
# CRITICAL INVARIANT: dry-run only, never tags. Only the manual primary release creates tags, so
# "since the last tag" == "since the last manual release" — that is what makes the eventual GitLab
# Release cover the whole accumulated range.

# Pure derivation, kept as a function so it can be unit-tested without invoking semantic-release.
# Args: <semantic-release-log> <last-tag-or-empty>  ->  prints the base primary version.
derive_primary_next() {
  log="$1"
  last_tag="$2"
  next="$(printf '%s\n' "$log" \
    | grep -i 'next release version is' \
    | grep -oE '[0-9]+\.[0-9]+\.[0-9]+[0-9A-Za-z.-]*' \
    | head -n1 || true)"
  if [ -z "$next" ]; then
    next="$(printf '%s' "$last_tag" | sed 's/^v//')"
    next="${next:-0.0.0}"
  fi
  # Strip any prerelease tail (feature branches resolve to the 'rc' channel): 1.3.0-rc.1 -> 1.3.0
  printf '%s' "${next%%-*}"
}

# Allow `VERSION_PREVIEW_LIB=1 . version-preview.sh` to load the function for tests without running.
[ "${VERSION_PREVIEW_LIB:-}" = "1" ] && return 0

# semantic-release needs full history + all tags to analyse commits since the last primary tag.
git fetch --tags --quiet 2>/dev/null || true

# RELEASE is unset here, so .releaserc.js skips @semantic-release/gitlab (no token, no tag).
LOG="$(semantic-release --dry-run 2>&1 || true)"
printf '%s\n' "$LOG"

LAST_TAG="$(git describe --tags --abbrev=0 2>/dev/null || true)"
PRIMARY_NEXT="$(derive_primary_next "$LOG" "$LAST_TAG")"

# Small rc counter that resets at each PRIMARY release: commits since the last primary tag.
# Consumed by publish_rc_images as <primary-next>-rc.<count>. Not a per-image counter — image-less
# merges (docs/CI) still advance it, so the sequence has gaps by design.
if [ -n "$LAST_TAG" ]; then
  RC_COUNT="$(git rev-list "${LAST_TAG}..HEAD" --count)"
else
  RC_COUNT="$(git rev-list HEAD --count)"   # no primary release yet: count from root
fi

echo "Projected next primary version: $PRIMARY_NEXT"
echo "PRIMARY_NEXT=$PRIMARY_NEXT" > preview.env
echo "RC_COUNT=$RC_COUNT" >> preview.env

# Visibility (main only): the actual rc image tag publish_rc_images will build.
# ponytail: mirrors the `-rc.` format in .docker_build_stage.yml (publish_rc_images before_script).
# `if` (not `[ ] && echo`) so a non-main run does not leave the script's exit code at 1.
if [ "${CI_COMMIT_BRANCH:-}" = "${CI_DEFAULT_BRANCH:-}" ]; then
  echo "Projected rc image tag: ${PRIMARY_NEXT}-rc.${RC_COUNT}"
fi
