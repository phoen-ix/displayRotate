#!/usr/bin/env bash
# Renders the docs screenshots: the docs tour in tests/DisplayRotate.DocsTests (Category=Docs; the
# rest of the project is the e2e suite, scripts/e2e.sh) builds the app's real windows and saves
# them as PNGs into $1 (default docs/screenshots). Windows only (Git Bash, or the
# docs-screenshots workflow on windows-latest): the windows need a desktop session to draw.
# The title bars show the latest release tag; DOCS_VERSION overrides it.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${OS:-}" != Windows_NT ]]; then
  echo "docs-screenshots: needs Windows. Run the docs-screenshots workflow (Actions tab) instead." >&2
  exit 1
fi

version=${DOCS_VERSION:-$(git describe --tags --abbrev=0 --match 'v[0-9]*.[0-9]*.[0-9]*' 2>/dev/null || echo v0.0.0)}
out=${1:-docs/screenshots}
mkdir -p "$out"
out=$(cd "$out" && pwd -W)
artifacts=${ARTIFACTS:-$(mktemp -d)}

echo "docs-screenshots: v${version#v} -> $out"
project=tests/DisplayRotate.DocsTests/DisplayRotate.DocsTests.csproj
dotnet build "$project" -c Release -p:Version="${version#v}"
DOCS_SCREENSHOT_DIR="$out" dotnet test --project "$project" -c Release --no-build --filter-trait "Category=Docs" --results-directory "$artifacts"
