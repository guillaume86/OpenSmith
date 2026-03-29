#!/usr/bin/env bash
# Collect a flamegraph-ready trace of the OpenSmith CLI processing a .csp file.
# Produces a .speedscope.json file that can be opened with https://www.speedscope.app
#
# Prerequisites (one-time):
#   dotnet tool install -g dotnet-trace
#
# Usage:
#   ./scripts/flamegraph.sh                              # trace DiffTest/SampleDb-Generator.csp
#   ./scripts/flamegraph.sh path/to/your.csp             # trace a specific .csp file
#   ./scripts/flamegraph.sh path/to/your.csp --no-cache  # trace without compilation cache

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI_PROJECT="$REPO_ROOT/Source/OpenSmith.Cli/OpenSmith.Cli.csproj"
OUT_DIR="$REPO_ROOT/artifacts/traces"

CSP_FILE="${1:-$REPO_ROOT/DiffTest/SampleDb-Generator.csp}"
shift 2>/dev/null || true
EXTRA_ARGS=("$@")

if ! command -v dotnet-trace &>/dev/null; then
    echo "dotnet-trace not found. Install with:"
    echo "  dotnet tool install -g dotnet-trace"
    exit 1
fi

mkdir -p "$OUT_DIR"

TIMESTAMP=$(date +%Y%m%d-%H%M%S)
CSP_NAME=$(basename "$CSP_FILE" .csp)
TRACE_FILE="$OUT_DIR/${CSP_NAME}-${TIMESTAMP}"

echo "Building in Release..."
dotnet build "$CLI_PROJECT" -c Release -v q --nologo

echo "Collecting trace for: $CSP_FILE"
echo "Output: ${TRACE_FILE}.speedscope.json"
echo ""

dotnet-trace collect \
    --format speedscope \
    --output "$TRACE_FILE" \
    -- dotnet run --project "$CLI_PROJECT" -c Release --no-build -- "$CSP_FILE" --verbose "${EXTRA_ARGS[@]}"

echo ""
echo "Trace saved to: ${TRACE_FILE}.speedscope.json"
echo "Open with: npx speedscope ${TRACE_FILE}.speedscope.json"
echo "  or upload to https://www.speedscope.app"
