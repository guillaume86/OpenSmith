#!/usr/bin/env bash
# Run benchmarks on two git refs and compare results.
# Useful for measuring the impact of a change.
#
# Usage:
#   ./scripts/bench-compare.sh main HEAD Compilation
#   ./scripts/bench-compare.sh abc123 def456 EndToEnd

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="Test/OpenSmith.Benchmarks/OpenSmith.Benchmarks.csproj"

if [ $# -lt 3 ]; then
    echo "Usage: $0 <base-ref> <head-ref> <filter>"
    echo "  e.g. $0 main HEAD Compilation"
    exit 1
fi

BASE_REF="$1"
HEAD_REF="$2"
FILTER="$3"
OUT_DIR="$REPO_ROOT/artifacts/bench-compare"
mkdir -p "$OUT_DIR"

run_bench() {
    local ref="$1"
    local label="$2"
    local export_dir="$OUT_DIR/$label"
    mkdir -p "$export_dir"

    echo "=== Benchmarking $label ($ref) ==="
    git stash --include-untracked -q 2>/dev/null || true
    git checkout "$ref" -q

    dotnet run --project "$PROJECT" -c Release -- \
        --filter "*${FILTER}*" \
        --exporters json \
        --artifacts "$export_dir"

    git checkout - -q
    git stash pop -q 2>/dev/null || true
}

cd "$REPO_ROOT"
run_bench "$BASE_REF" "base"
run_bench "$HEAD_REF" "head"

echo ""
echo "Results saved to:"
echo "  Base: $OUT_DIR/base/"
echo "  Head: $OUT_DIR/head/"
echo ""
echo "Compare the JSON exports in each artifacts directory."
echo "Or use dotnet-benchmark-compare: https://github.com/nickvdyck/dotnet-benchmark-compare"
