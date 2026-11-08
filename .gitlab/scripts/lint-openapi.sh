#!/bin/sh
# POSIX shell: avoid requiring bash in minimal images
# Usage: lint-openapi.sh [OPENAPI_FILE...]
#   One or more OpenAPI spec files. Defaults to the build-generated User, Administration and Service documents.

if [ "$#" -eq 0 ]; then
  set -- API_user.json API_administration.json API_service.json
fi

# Lint OpenAPI with Spectral and generate GitLab-friendly JUnit + markdown report
mkdir -p test-results spectral-reports

# Run Spectral linting and keep output for diagnostics
SPECTRAL_OUTPUT_LOG="spectral-reports/spectral-output.log"
SPECTRAL_JUNIT_REPORT="spectral-reports/spectral-report.xml"
SPECTRAL_FINAL_JUNIT="test-results/spectral-report.xml"
spectral lint -D -f junit -o "$SPECTRAL_JUNIT_REPORT" --ruleset ./.spectral.yml "$@" > "$SPECTRAL_OUTPUT_LOG" 2>&1
SPECTRAL_EXIT_CODE=$?

# If Spectral failed to execute (invalid ruleset, parser/runtime error), fail the job with explicit JUnit failure.
# Lint findings can still return non-zero, but in that case Spectral still writes a JUnit report.
if [ "$SPECTRAL_EXIT_CODE" -ne 0 ] && [ ! -f "$SPECTRAL_JUNIT_REPORT" ]; then
  echo "Error running Spectral!"
  cat "$SPECTRAL_OUTPUT_LOG"

  mkdir -p test-results
  {
    echo "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
    echo "<testsuite name=\"OpenAPI.Spectral\" tests=\"1\" failures=\"1\">"
    echo "  <testcase classname=\"OpenAPI.Spectral\" name=\"spectral-runtime\">"
    echo "    <failure message=\"Spectral execution error\">See job log and spectral-reports/spectral-output.log.</failure>"
    echo "  </testcase>"
    echo "</testsuite>"
  } > "$SPECTRAL_FINAL_JUNIT"

  {
    echo "## 📋 OpenAPI Specification Lint Report"
    echo ""
    echo "❌ Spectral failed to execute (ruleset/config/runtime error)."
    echo ""
    echo "See \`spectral-reports/spectral-output.log\` for details."
  } > spectral-reports/lint-report.md

  cat spectral-reports/lint-report.md
  exit 1
fi

# If no python interpreter is available, skip conversion and emit empty JUnit (avoid installing at runtime)
if ! command -v python3 >/dev/null 2>&1 && ! command -v python >/dev/null 2>&1 ; then
  echo "Python not found; skipping JUnit conversion. Consider baking python3 into the job image or BASE_IMAGE." > spectral-reports/lint-report.md
  mkdir -p test-results
  echo "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" > test-results/spectral-report.xml
  echo "<testsuite name=\"OpenAPI.Spectral\" tests=\"0\" failures=\"0\"/>" >> test-results/spectral-report.xml
  echo "No python interpreter available; wrote empty JUnit" >> spectral-reports/lint-report.md
  echo "Spectral skipped: python missing"
  exit 0
fi

# Convert JUnit to ensure classname exists
if [ -f "$SPECTRAL_JUNIT_REPORT" ]; then
  python3 - << 'PY'
import xml.etree.ElementTree as ET
try:
    tree = ET.parse('spectral-reports/spectral-report.xml')
    root = tree.getroot()
    for testcase in root.findall('.//testcase'):
        if 'classname' not in testcase.attrib:
            testcase.set('classname', 'OpenAPI.Spectral')
    if 'name' not in root.attrib:
        root.set('name', 'OpenAPI.Spectral')
    tree.write('test-results/spectral-report.xml', encoding='utf-8', xml_declaration=True)
    print('✅ JUnit report converted to GitLab format')
except Exception as e:
    print('⚠️ Error converting report:', e)
PY
fi

# Create markdown report
echo "## 📋 OpenAPI Specification Lint Report" > spectral-reports/lint-report.md
echo "" >> spectral-reports/lint-report.md

ERRORS=0
WARNINGS=0

if [ -f test-results/spectral-report.xml ]; then
  echo "✅ OpenAPI specification linting completed" >> spectral-reports/lint-report.md
  echo "" >> spectral-reports/lint-report.md
  ERRORS=$(grep -o '<failure' test-results/spectral-report.xml 2>/dev/null | wc -l || true)
  WARNINGS=$(grep -o '<skipped' test-results/spectral-report.xml 2>/dev/null | wc -l || true)
  if [ "$ERRORS" -gt 0 ] || [ "$WARNINGS" -gt 0 ]; then
    echo "### 📊 Issue Summary" >> spectral-reports/lint-report.md
    echo "| Issue Type | Count |" >> spectral-reports/lint-report.md
    echo "|------------|-------|" >> spectral-reports/lint-report.md
    echo "| 🔴 Errors | $ERRORS |" >> spectral-reports/lint-report.md
    echo "| 🟡 Warnings | $WARNINGS |" >> spectral-reports/lint-report.md
  else
    echo "**No linting issues found!** ✨" >> spectral-reports/lint-report.md
  fi
fi

echo ""
cat spectral-reports/lint-report.md

# Ensure JUnit exists so GitLab shows test tab
if [ -f test-results/spectral-report.xml ]; then
  echo "Spectral JUnit exists"
  # Exit with error if issues found
  if [ "$ERRORS" -gt 0 ]; then
    echo "❌ OpenAPI linting failed with $ERRORS error(s)"
    exit 1
  fi
  exit 0
else
  mkdir -p test-results
  echo "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" > test-results/spectral-report.xml
  echo "<testsuite name=\"OpenAPI.Spectral\" tests=\"0\" failures=\"0\"/>" >> test-results/spectral-report.xml
  echo 'No spectral report found; wrote empty JUnit'
  exit 0
fi
