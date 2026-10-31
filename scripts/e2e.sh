#!/usr/bin/env bash
# Runs the e2e suite (tests/COVERAGE.md): every test in tests/DisplayRotate.DocsTests except the
# docs tour drives the real tray app, Settings dialog, Identify overlay and DisplayRotate.exe on
# this desktop. Windows only (Git Bash, or ci.yml's e2e job on windows-latest). No stack and no
# ports, so PORT_BASE and compose project names do not apply. Test results go to $ARTIFACTS.
# The tests use this account's settings file and autostart value and put both back; the tray
# tests skip while a real DisplayRotate runs. Extra arguments go to the test runner.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${OS:-}" != Windows_NT ]]; then
  echo "e2e: needs Windows. ci.yml's e2e job runs it on windows-latest." >&2
  exit 1
fi

artifacts=${ARTIFACTS:-$(mktemp -d)}
mkdir -p "$artifacts"
project=tests/DisplayRotate.DocsTests/DisplayRotate.DocsTests.csproj
# A release version: a 0.0.0 build is never offered an update, and the update flows need one.
dotnet build "$project" -c Release -p:Version=9.9.9
dotnet test --project "$project" -c Release --no-build --filter-not-trait "Category=Docs" \
  --results-directory "$artifacts" --report-xunit-html "$@"
