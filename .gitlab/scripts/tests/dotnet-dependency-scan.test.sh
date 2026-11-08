#!/usr/bin/env bash
set -euo pipefail

# Self-check for the pure helpers in dotnet-dependency-scan.sh — completeness_errors(), the guard that
# turns a PARTIAL vulnerability scan into a job failure instead of a silent pass, and
# suppressed_advisory_urls(). Needs no jq, dotnet or network.
# Run: bash .gitlab/scripts/tests/dotnet-dependency-scan.test.sh
DOTNET_DEPENDENCY_SCAN_LIB=1 . "$(dirname "$0")/../dotnet-dependency-scan.sh"

fixtures=$(mktemp -d)
trap 'rm -rf "$fixtures"' EXIT

fails=0
assert() { # <desc> <expected> <actual>
  if [ "$2" = "$3" ]; then
    echo "ok: $1"
  else
    echo "FAIL: $1 (expected '$2', got '$3')"
    fails=$((fails + 1))
  fi
}

# A complete scan reports nothing — this is the only input that may leave the gate green.
assert "complete scan is silent" "0" \
  "$(completeness_errors '' 47 47 | grep -c . || true)"

# An unaudited project (no assets file -> problems[]) must surface, or its packages go unchecked.
assert "unaudited project surfaces" "1" \
  "$(completeness_errors 'Tests/Core/X.csproj: No assets file was found' 47 47 | grep -c . || true)"

# A .csproj that is not in the solution is never enumerated, so counts diverging must surface too.
assert "project outside the solution surfaces" "1" \
  "$(completeness_errors '' 46 47 | grep -c . || true)"

# A zero on-disk count means the .csproj enumeration itself failed — must not read as complete.
assert "unusable on-disk count surfaces" "1" \
  "$(completeness_errors '' 47 0 | grep -c . || true)"

# Both blind spots at once are both reported, not collapsed to one.
assert "both reasons reported" "2" \
  "$(completeness_errors 'Tests/Core/X.csproj: No assets file was found' 46 47 | grep -c . || true)"

cat > "$fixtures/two-suppressions.props" <<'PROPS'
<Project>
  <ItemGroup>
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-1111-1111-1111" />
    <NuGetAuditSuppress Include="https://github.com/advisories/GHSA-2222-2222-2222" />
  </ItemGroup>
</Project>
PROPS
printf '<Project>\n  <ItemGroup />\n</Project>\n' > "$fixtures/no-suppressions.props"

# Every accepted advisory must reach the gate's filter, or a suppressed CVE fails the job forever.
assert "every suppressed advisory url is extracted" \
  $'https://github.com/advisories/GHSA-1111-1111-1111\nhttps://github.com/advisories/GHSA-2222-2222-2222' \
  "$(suppressed_advisory_urls "$fixtures/two-suppressions.props")"

# Suppressing nothing is a legitimate (and desirable) state. grep exits 1 when it matches nothing, and
# the call site feeds a pipeline under `pipefail`, so an unguarded grep kills the job before the gate.
suppressed_advisory_urls "$fixtures/no-suppressions.props" > "$fixtures/none.out" && status=0 || status=$?
assert "no suppressions exits 0" "0" "$status"
# Not even a blank line: `jq -R .` would turn one into an empty-string entry in the suppressed array.
assert "no suppressions extracts nothing" "0" "$(wc -l < "$fixtures/none.out" | tr -d ' ')"

if [ "$fails" -ne 0 ]; then
  echo "$fails check(s) failed"
  exit 1
fi
echo "all dotnet-dependency-scan checks passed"
