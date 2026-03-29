#!/usr/bin/env bash
# Run BenchmarkDotNet benchmarks.
#
# Usage:
#   ./scripts/bench.sh                     # list all benchmarks
#   ./scripts/bench.sh Compilation         # run benchmarks matching "Compilation"
#   ./scripts/bench.sh CachedCompilation   # cache hit vs cold compile
#   ./scripts/bench.sh EndToEnd            # full pipeline
#   ./scripts/bench.sh --all               # run every benchmark (slow)
#
# Extra args are forwarded to BenchmarkDotNet:
#   ./scripts/bench.sh Compilation --iterationCount 10

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/Test/OpenSmith.Benchmarks/OpenSmith.Benchmarks.csproj"

if [ $# -eq 0 ]; then
    echo "Available benchmarks:"
    dotnet run --project "$PROJECT" -c Release -- --list flat
    echo ""
    echo "Usage: $0 <filter> [extra-args...]"
    echo "  e.g. $0 Compilation"
    exit 0
fi

FILTER="$1"
shift

if [ "$FILTER" = "--all" ]; then
    dotnet run --project "$PROJECT" -c Release -- "$@"
else
    dotnet run --project "$PROJECT" -c Release -- --filter "*${FILTER}*" "$@"
fi
