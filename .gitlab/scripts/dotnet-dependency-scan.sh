#!/usr/bin/env bash
set -euo pipefail

# .NET dependency vulnerability scan + CycloneDX SBOM generation.
#
# Gate:  `dotnet list package --vulnerable --include-transitive` resolves direct + transitive
#        NuGet packages and checks them against the NuGet audit / GitHub Advisory feed at build
#        time. It needs no lockfile and works on any GitLab tier. We parse its JSON output and
#        FAIL the job on any vulnerability.
# SBOM:  `dotnet CycloneDX` emits a CycloneDX BOM (gl-sbom-nuget-nuget.cdx.json) uploaded as a
#        `cyclonedx` report so the GitLab Dependency List / Continuous Vulnerability Scanning can
#        ingest the inventory. SBOM generation is best-effort and does NOT gate.
#
# Usage: dotnet-dependency-scan.sh [SOLUTION] [SBOM_PROJECT]
#   SOLUTION      Solution to scan for vulnerable packages (default: UserOrganization.slnx)
#   SBOM_PROJECT  Project whose restore closure seeds the SBOM (default: src/Core/API/API.csproj)

SOLUTION="${1:-UserOrganization.slnx}"
SBOM_PROJECT="${2:-src/Core/API/API.csproj}"
SBOM_FILE="gl-sbom-nuget-nuget.cdx.json"

# Completeness decision, kept pure (no jq, no dotnet) so the self-check can exercise it.
# <problems_text> <enumerated_count> <on_disk_count> — echoes one line per reason the scan is partial,
# nothing at all when it is complete.
completeness_errors() {
  local problems="$1" enumerated="$2" on_disk="$3"
  [ -n "$problems" ] && printf '%s\n' "$problems"
  if [ "$enumerated" -ne "$on_disk" ]; then
    printf '%s\n' "the solution enumerated $enumerated project(s) but $on_disk .csproj exist in the checkout — a project outside the solution is never scanned"
  fi
  return 0
}

# <props_file> — echoes one explicitly suppressed advisory url per line, nothing when there are none.
# The `|| true` is load-bearing: `grep` exits 1 on zero matches, which would kill the whole script
# through the caller's `pipefail` pipeline — suppressing no advisory at all is a legitimate state.
suppressed_advisory_urls() {
  grep -oP '(?<=<NuGetAuditSuppress Include=")[^"]+' "$1" || true
}

# Allow `DOTNET_DEPENDENCY_SCAN_LIB=1 . dotnet-dependency-scan.sh` to load the functions for tests.
[ "${DOTNET_DEPENDENCY_SCAN_LIB:-}" = "1" ] && return 0

mkdir -p test-results

echo "🔄 Restoring $SOLUTION..."
dotnet restore "$SOLUTION"

# ── Vulnerability gate ────────────────────────────────────────────────────────
echo "🔍 Scanning for vulnerable NuGet packages (direct + transitive)..."
VULN_JSON="vulnerable-packages.json"
# `--vulnerable` exits 0 only when it could read EVERY project's assets file; a project it could not
# read lands in `problems[]` UNAUDITED and the command exits non-zero. The gate is decided from the
# parsed JSON below, but a non-zero exit here means the scan is PARTIAL — see the completeness guard.
scan_solution() {
  dotnet list "$SOLUTION" package --vulnerable --include-transitive --format json --output-version 1 > "$VULN_JSON"
}

# A project excluded from the Debug build in the .slnx (`<Build Solution="Debug|*" Project="false" />`)
# is excluded from solution-level restore too, so it has no assets file and is silently skipped by the
# scan. Restoring such a project on its own does work, so retry once — best-effort; the authoritative
# check is the completeness guard below.
if ! scan_solution; then
  echo "⚠️  Partial scan — restoring the project(s) the solution-level restore skipped..."
  # `|| true` inside the group, or `pipefail` would propagate jq's exit on unparseable output and kill
  # the script here — that path must fall through to the completeness guard, which reports it properly.
  { jq -r '.problems[]? | select(.level == "error") | .project' "$VULN_JSON" 2>/dev/null || true; } | sort -u |
    while IFS= read -r proj; do
      [ -n "$proj" ] || continue
      echo "   ↻ $proj"
      dotnet restore "$proj" || true
    done
  scan_solution || true
fi

# `dotnet list package --vulnerable` does NOT honor <NuGetAuditSuppress> (that only silences the
# NU1903 restore warning), so advisories explicitly suppressed in Directory.Packages.props must be
# filtered out here too, or every reviewed-and-accepted CVE fails this gate forever.
SUPPRESSED_JSON=$(suppressed_advisory_urls Directory.Packages.props | jq -R . | jq -s .)

# Parsing uses `jq` (baked into the dotnet-dev-base image) instead of python3, which is not
# present and cannot be apt-get-installed on the non-root CI runner. Findings are flattened to TSV,
# then bash builds the JUnit report and decides the gate. A malformed/empty JSON yields no findings
# (jq failure is tolerated with `|| true`), mirroring the previous parse-error fallback.
GATE=0
FINDINGS_TSV="findings.tsv"
jq -r --argjson suppressed "$SUPPRESSED_JSON" '
  [ .projects[]?                                      as $p
    | $p.frameworks[]?                                as $fw
    | ( {kind:"top-level",  arr:($fw.topLevelPackages   // [])},
        {kind:"transitive", arr:($fw.transitivePackages // [])} ) as $g
    | $g.arr[]?                                       as $pkg
    | $pkg.vulnerabilities[]?                         as $v
    | select(($v.advisoryurl // "") as $u | $suppressed | index($u) | not)
    | { project: ($p.path // "?"), tfm: ($fw.framework // "?"), kind: $g.kind,
        id: ($pkg.id // "?"), version: ($pkg.resolvedVersion // "?"),
        severity: ($v.severity // "unknown"), url: ($v.advisoryurl // "") } ]
  | .[] | [.project, .tfm, .kind, .id, .version, .severity, .url] | @tsv
' "$VULN_JSON" > "$FINDINGS_TSV" 2>/dev/null || true

# `grep -c` prints the count itself and exits non-zero on zero matches, so swallow its status
# (don't echo a second 0) and default to 0 if the file is absent.
COUNT=$(grep -c . "$FINDINGS_TSV" 2>/dev/null || true)
COUNT=${COUNT:-0}

# ── Completeness guard ────────────────────────────────────────────────────────
# A scan that could not audit every project must FAIL, not report success on the projects it happened
# to reach. `--vulnerable` legitimately omits the `frameworks` key for clean projects, so "no findings"
# is never by itself proof of a clean run. Completeness = parseable output + zero error-level problems
# + every .csproj in the checkout present in `projects[]` (one outside the solution is never scanned).
SCANNED=0
if ! jq -e 'type == "object"' "$VULN_JSON" >/dev/null 2>&1; then
  SCAN_ERRORS="$VULN_JSON is missing or unparseable — dotnet list failed before reporting anything"
else
  PROBLEMS=$(jq -r '.problems[]? | select(.level == "error") | "\(.project): \(.text)"' "$VULN_JSON")
  SCANNED=$(jq -r '[.projects[]?.path] | length' "$VULN_JSON")
  # `find`, not `git ls-files`: the runner clones as one user and the job runs as non-root `app`, so
  # git refuses the checkout with "detected dubious ownership" and would report zero projects on disk.
  # `artifacts/` is the UseArtifactsOutput tree and `.claude/` holds nested worktrees — neither holds
  # a project that belongs to this solution.
  ON_DISK=$(find . -name '*.csproj' -not -path './artifacts/*' -not -path './.claude/*' | grep -c . || true)
  SCAN_ERRORS=$(completeness_errors "$PROBLEMS" "$SCANNED" "${ON_DISK:-0}")
fi

ERR_COUNT=$(printf '%s' "$SCAN_ERRORS" | grep -c . || true)
ERR_COUNT=${ERR_COUNT:-0}
TOTAL=$((COUNT + ERR_COUNT))

# Escape XML metacharacters (incl. quotes, so the same helper is safe in attributes and text).
xml_escape() { sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g'; }

{
  echo '<?xml version="1.0" encoding="UTF-8"?>'
  if [ "$TOTAL" -eq 0 ]; then
    echo '<testsuite name="dotnet-dependency-scan" tests="1" failures="0">'
    echo '  <testcase classname="Security.Dependencies" name="No vulnerable packages" time="0"/>'
  else
    echo "<testsuite name=\"dotnet-dependency-scan\" tests=\"$TOTAL\" failures=\"$TOTAL\">"
    if [ "$ERR_COUNT" -gt 0 ]; then
      printf '%s\n' "$SCAN_ERRORS" | while IFS= read -r line; do
        [ -n "$line" ] || continue
        echo '  <testcase classname="Security.Dependencies" name="Scan completeness" time="0">'
        echo "    <failure message=\"Incomplete dependency scan\">$(printf '%s' "$line" | xml_escape)</failure>"
        echo '  </testcase>'
      done
    fi
    while IFS=$'\t' read -r project tfm kind id version severity url; do
      name=$(printf '%s %s (%s)' "$id" "$version" "$severity" | xml_escape)
      msg=$(printf '%s vulnerability in %s package %s %s' "$severity" "$kind" "$id" "$version" | xml_escape)
      body=$(printf '%s [%s] %s %s %s - %s - %s' "$project" "$tfm" "$kind" "$id" "$version" "$severity" "$url" | xml_escape)
      echo "  <testcase classname=\"Security.Dependencies\" name=\"$name\" time=\"0\">"
      echo "    <failure message=\"$msg\">$body</failure>"
      echo "  </testcase>"
    done < "$FINDINGS_TSV"
  fi
  echo '</testsuite>'
} > test-results/dependency-scanning.xml

if [ "$ERR_COUNT" -gt 0 ]; then
  echo "FAIL: INCOMPLETE scan — a partial scan is not a pass:"
  printf '%s\n' "$SCAN_ERRORS" | sed 's/^/   - /'
  GATE=1
fi

if [ "$COUNT" -gt 0 ]; then
  echo "FAIL: $COUNT vulnerable package reference(s) found:"
  while IFS=$'\t' read -r project tfm kind id version severity url; do
    echo "   - [$severity] $kind $id $version ($project $tfm)"
    [ -n "$url" ] && echo "       $url"
  done < "$FINDINGS_TSV"
  GATE=1
elif [ "$ERR_COUNT" -eq 0 ]; then
  echo "OK: No vulnerable NuGet packages detected across $SCANNED project(s)."
fi

# ── CycloneDX SBOM (best-effort inventory; does not gate) ──────────────────────
echo "📦 Generating CycloneDX SBOM from $SBOM_PROJECT..."
if dotnet tool install --global CycloneDX >/dev/null 2>&1 || dotnet tool update --global CycloneDX >/dev/null 2>&1; then
  export PATH="$PATH:$HOME/.dotnet/tools"
  if dotnet CycloneDX "$SBOM_PROJECT" --json --output . --filename "$SBOM_FILE"; then
    echo "✅ SBOM written to $SBOM_FILE"
  else
    echo "⚠️  SBOM generation failed (inventory only — not failing the job)."
  fi
else
  echo "⚠️  Could not install the CycloneDX tool (inventory only — not failing the job)."
fi

exit $GATE
