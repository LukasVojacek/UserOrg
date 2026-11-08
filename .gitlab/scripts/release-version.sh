#!/usr/bin/env bash
set -euo pipefail

# PRIMARY release: full semantic-release run (git tag X.Y.Z + GitLab Release via .releaserc.js when
# RELEASE=true) that also writes release.env (VERSION=X.Y.Z) through @semantic-release/exec.
# Consumed by publish_primary_images. Must run with GIT_DEPTH:0 so the whole range since the last
# primary tag is analysed.

semantic-release

# If semantic-release (publishCmd/failCmd) already wrote release.env, keep it. Otherwise derive it
# from the latest tag (strip leading 'v').
if [ ! -f release.env ]; then
  LATEST_TAG_RAW=$(git describe --tags --abbrev=0 2>/dev/null || echo "unknown")
  LATEST_TAG="${LATEST_TAG_RAW#v}"
  echo "VERSION=$LATEST_TAG" > release.env
  echo "VERSION_CHANGED=false" >> release.env
fi

cat release.env
