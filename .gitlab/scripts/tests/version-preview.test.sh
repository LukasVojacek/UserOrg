#!/usr/bin/env bash
set -euo pipefail

# Self-check for derive_primary_next() in version-preview.sh — runs the pure version-derivation
# logic without invoking semantic-release. Run: bash .gitlab/scripts/tests/version-preview.test.sh
VERSION_PREVIEW_LIB=1 . "$(dirname "$0")/../version-preview.sh"

fails=0
assert() { # <desc> <expected> <actual>
  if [ "$2" = "$3" ]; then
    echo "ok: $1"
  else
    echo "FAIL: $1 (expected '$2', got '$3')"
    fails=$((fails + 1))
  fi
}

assert "plain next version"        "1.3.0" "$(derive_primary_next 'The next release version is 1.3.0' '')"
assert "strip rc prerelease tail"  "1.3.0" "$(derive_primary_next 'next release version is 1.3.0-rc.1' '')"
assert "fallback to last tag"      "6.8.3" "$(derive_primary_next 'no release this run' 'v6.8.3')"
assert "fallback default 0.0.0"    "0.0.0" "$(derive_primary_next 'nothing here' '')"

if [ "$fails" -ne 0 ]; then
  echo "$fails check(s) failed"
  exit 1
fi
echo "all version-preview checks passed"
