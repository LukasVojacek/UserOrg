#!/bin/bash
set -euo pipefail

# OpenAPI diff script — detects breaking API changes between HEAD~1 and HEAD.
#
# The spec is generated at build time and committed at the repo root, so a previous revision
# exists to diff against. allow_failure: it reports breaking changes, never blocks.

if [ "$#" -eq 0 ]; then
  set -- API_user.json
fi

mkdir -p test-results
junit_file="test-results/openapi-diff.xml"
echo '<?xml version="1.0" encoding="UTF-8"?>' > "$junit_file"
echo '<testsuites>' >> "$junit_file"

total_changes=0

for OPENAPI_FILE in "$@"; do
  echo "=== ${OPENAPI_FILE}"
  previous_spec="$(basename "$OPENAPI_FILE").previous"
  breaking_output=$(mktemp)

  # Check if OpenAPI file actually changed between HEAD~1 and HEAD
  if ! git diff HEAD~1 HEAD --quiet -- "$OPENAPI_FILE" 2>/dev/null; then
    echo "📝 OpenAPI file changed, checking for breaking changes..."
    if git show HEAD~1:"${OPENAPI_FILE}" > "${previous_spec}" 2>/dev/null; then
      echo "✓ Previous OpenAPI spec retrieved from HEAD~1"
      oasdiff breaking "${previous_spec}" "${OPENAPI_FILE}" 2>&1 | tee "${breaking_output}" || true
      echo ""
      oasdiff summary "${previous_spec}" "${OPENAPI_FILE}" || true
    else
      echo "ℹ No previous OpenAPI spec found (first commit or file not in HEAD~1 / not tracked)"
    fi
  else
    echo "✅ No OpenAPI changes detected between HEAD~1 and HEAD - skipping diff"
  fi

  # Count breaking changes from oasdiff output
  # oasdiff breaking outputs lines starting with "error" for each breaking change
  num_changes=$(grep -c "^error[[:space:]]" "${breaking_output}" || true)
  total_changes=$(( total_changes + num_changes ))

  suite="OpenAPI.Breaking.$(basename "$OPENAPI_FILE" .json)"
  test_count=$(( num_changes > 0 ? num_changes : 1 ))
  echo "  <testsuite name=\"$suite\" tests=\"$test_count\">" >> "$junit_file"

  if [ "${num_changes}" -gt 0 ]; then
    while IFS= read -r line; do
      if [[ "$line" =~ ^error[[:space:]] ]]; then
        error_msg=$(echo "$line" | sed 's/^error[[:space:]]*//')
        error_msg=$(echo "$error_msg" | sed 's/&/\&amp;/g; s/</\&lt;/g; s/>/\&gt;/g; s/"/\&quot;/g')
        echo "    <testcase classname=\"$suite\" name=\"Breaking change\" time=\"0\">" >> "$junit_file"
        echo "      <failure message=\"$error_msg\">API breaking change detected</failure>" >> "$junit_file"
        echo "    </testcase>" >> "$junit_file"
      fi
    done < "${breaking_output}"
  else
    echo "    <testcase classname=\"$suite\" name=\"No breaking changes\" time=\"0\"/>" >> "$junit_file"
  fi

  echo "  </testsuite>" >> "$junit_file"
  rm -f "${breaking_output}"
done

echo '</testsuites>' >> "$junit_file"

# Exit with error if breaking changes detected
[ "${total_changes}" -eq 0 ] || exit 1
