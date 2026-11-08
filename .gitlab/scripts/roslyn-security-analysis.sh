#!/usr/bin/env bash
set -euo pipefail

# Roslyn security analysis script for .NET solutions
# Usage: roslyn-security-analysis.sh [SOLUTION] [SOURCE_PATHS...]
#   SOLUTION      Path to solution file (default: UserOrganization.slnx)
#   SOURCE_PATHS  Source paths to analyze (informational; default: src)

SOLUTION="${1:-UserOrganization.slnx}"
shift || true
SOURCE_PATHS=("${@:-src}")

echo "🔍 Running Roslyn security analyzers on C# code..."
mkdir -p security-reports
mkdir -p test-results

# Run analyzers via dotnet build
ANALYZER_OUTPUT="security-reports/analyzer-output.log"
BUILD_FAILED=0

echo "Building solution with security analyzers..."
# Restore here: the `build` job's artifacts ship bin/Release/** but NOT obj/project.assets.json
# (NuGet restore output lives at obj/ root), so --no-restore would fail with NETSDK1004. The
# NUGET cache keeps this fast.
dotnet build "./$SOLUTION" --configuration Release -p:TreatWarningsAsErrors=false 2>&1 | tee "$ANALYZER_OUTPUT" || BUILD_FAILED=$?

# Extract analyzer warnings/errors (format: /path/file.cs(line,col): warning/error RULE: message)
FINDINGS=()
while IFS= read -r line; do
  if [[ $line =~ ^/.*\.cs\([0-9]+,[0-9]+\):.*: ]]; then
    FINDINGS+=("$line")
  fi
done < "$ANALYZER_OUTPUT"

FINDING_COUNT=${#FINDINGS[@]}

# Build analysis markdown
cat > security-reports/analysis.md <<'MD'
## 🔒 Roslyn Security Analysis Report

### Code Analysis Checks
| Check | Result |
|-------|--------|
MD

if [ "$BUILD_FAILED" -ne 0 ]; then
  echo "| 🔒 Security Analyzers | ❌ Build failed — analysis did not run |" >> security-reports/analysis.md
  echo "" >> security-reports/analysis.md
  echo "**The solution did not compile, so the analyzers produced no verdict. A green result here would be meaningless — failing the job. See analyzer-output.log.**" >> security-reports/analysis.md
elif [ $FINDING_COUNT -eq 0 ]; then
  echo "| 🔒 Security Analyzers | ✅ Pass |" >> security-reports/analysis.md
  echo "" >> security-reports/analysis.md
  echo "**No security issues detected**" >> security-reports/analysis.md
else
  echo "| 🔒 Security Analyzers | ⚠️ Found $FINDING_COUNT issue(s) |" >> security-reports/analysis.md
  echo "" >> security-reports/analysis.md
  echo "### Detected Issues:" >> security-reports/analysis.md
  echo "" >> security-reports/analysis.md
  for finding in "${FINDINGS[@]}"; do
    echo "- $finding" >> security-reports/analysis.md
  done
fi

echo "" >> security-reports/analysis.md
echo "See analyzer-output.log for detailed diagnostics" >> security-reports/analysis.md

echo ""
cat security-reports/analysis.md

# Generate JUnit report with individual testcases per finding
{
  echo '<?xml version="1.0" encoding="UTF-8"?>'
  if [ "$BUILD_FAILED" -ne 0 ]; then
    echo '<testsuite name="roslyn-security-analysis" tests="1" failures="1">'
    echo '  <testcase classname="Security.Analysis" name="Build" time="0">'
    echo '    <failure message="Build failed">Solution did not compile; security analyzers did not run.</failure>'
    echo '  </testcase>'
  elif [ $FINDING_COUNT -eq 0 ]; then
    echo '<testsuite name="roslyn-security-analysis" tests="1" failures="0">'
    echo '  <testcase classname="Security.Analysis" name="Security analyzers" time="0"/>'
  else
    echo "<testsuite name=\"roslyn-security-analysis\" tests=\"$FINDING_COUNT\" failures=\"$FINDING_COUNT\">"
    i=0
    for finding in "${FINDINGS[@]}"; do
      ((i++))
      # Escape special XML chars
      escaped_finding=$(echo "$finding" | sed 's/&/\&amp;/g; s/</\&lt;/g; s/>/\&gt;/g; s/"/\&quot;/g')
      echo "  <testcase classname=\"Security.Analysis\" name=\"Issue $i\" time=\"0\">"
      echo "    <failure message=\"Security analyzer finding\">$escaped_finding</failure>"
      echo "  </testcase>"
    done
  fi
  echo '</testsuite>'
} > test-results/roslyn-security.xml

EXIT_CODE=0
if [ "$BUILD_FAILED" -ne 0 ]; then
  echo ""
  echo "❌ Build failed (exit $BUILD_FAILED) — security analyzers did not run on a compiled solution."
  echo "   A green result here would be meaningless, so the job is failing. See $ANALYZER_OUTPUT."
  EXIT_CODE=1
elif [ $FINDING_COUNT -gt 0 ]; then
  echo ""
  echo "❌ Security analysis found $FINDING_COUNT issue(s)"
  EXIT_CODE=1
else
  echo ""
  echo "✅ Security analysis complete - no issues found"
fi

exit $EXIT_CODE
