#!/usr/bin/env bash
# Run a specific BenchmarkDotNet benchmark with the ETW profiler to produce
# an .etl trace file (Windows) that can be analyzed with PerfView.
#
# Usage:
#   ./scripts/flamegraph-bench.sh Compilation    # profile compilation benchmarks
#   ./scripts/flamegraph-bench.sh EndToEnd       # profile end-to-end pipeline
#
# The .etl files are written to BenchmarkDotNet.Artifacts/ in the repo root.
# Open with PerfView: https://github.com/microsoft/perfview

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$REPO_ROOT/opensmith/test/OpenSmith.Benchmarks/OpenSmith.Benchmarks.csproj"

if [ $# -eq 0 ]; then
    echo "Usage: $0 <filter> [extra-args...]"
    echo "  e.g. $0 Compilation"
    echo ""
    echo "Runs benchmarks with ETW profiler attached (Windows only)."
    echo "Results in BenchmarkDotNet.Artifacts/"
    exit 0
fi

FILTER="$1"
shift

echo "Running benchmarks with ETW profiler (may require elevation)..."
dotnet run --project "$PROJECT" -c Release -- --filter "*${FILTER}*" --profiler ETW "$@"

echo ""
echo "ETL trace files are in: $REPO_ROOT/BenchmarkDotNet.Artifacts/"
echo "Open with PerfView for flamegraph analysis."
