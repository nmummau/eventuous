#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
project="${1:?Usage: bash scripts/test-with-coverage.sh path/to/project.csproj [dotnet test options]}"
shift
test -f "$project"
project_name="$(basename -- "$project" .csproj)"
output="${COVERAGE_OUTPUT_DIR:-$repo_root/coverage-results}/$project_name"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
# A failed rebuild must not leave coverage from an earlier local run looking current.
rm -f -- "$output/coverage.cobertura.xml" "$output/run.json"

started=$SECONDS
status=0
dotnet test --project "$project" -c "${CONFIGURATION:-Debug CI}" -f net10.0 "$@" -- \
  --coverage --coverage-settings "$repo_root/.github/coverage.config.xml" \
  --coverage-output-format cobertura --coverage-output "$output/coverage.cobertura.xml" || status=$?

python3 - "$output/run.json" "$status" "$((SECONDS - started))" <<'PY'
import json
import sys
from pathlib import Path

Path(sys.argv[1]).write_text(json.dumps({"exit_code": int(sys.argv[2]), "duration_seconds": int(sys.argv[3])}) + "\n")
PY

if [[ ! -s "$output/coverage.cobertura.xml" ]]; then
  echo "::error::No coverage produced for $project_name (test exit code $status)"
  if [[ "$status" -eq 0 ]]; then status=1; fi
fi
exit "$status"
