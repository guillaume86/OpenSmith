---
name: bench
description: Run BenchmarkDotNet benchmarks, collect flamegraphs, or compare performance between git refs. TRIGGER when user asks to benchmark, profile, measure performance, generate flamegraphs, or compare perf.
allowed-tools: Bash(*), Read, Grep, Glob
---

# Benchmarking OpenSmith

## Project layout

- Benchmark project: `Test/OpenSmith.Benchmarks/OpenSmith.Benchmarks.csproj`
- Helper scripts: `scripts/bench.sh`, `scripts/flamegraph.sh`, `scripts/flamegraph-bench.sh`, `scripts/bench-compare.sh`
- Artifacts are written to `BenchmarkDotNet.Artifacts/` (gitignored) and `artifacts/` (gitignored)

## Available benchmarks

| Benchmark class | What it measures |
|---|---|
| `CompilationBenchmarks` | Roslyn compilation of generated template C# sources (the main bottleneck) |
| `CachedCompilationBenchmarks` | Cold Roslyn compile vs disk-cache hit |
| `TemplateRegistryBenchmarks` | Recursive .cst file resolution + parsing |
| `CstParserBenchmarks` | Isolated CstParser.Parse on pre-loaded content |
| `CodeGenerationBenchmarks` | C# source emission from parsed templates |
| `CspParserBenchmarks` | CSP XML project file parsing |
| `EndToEndBenchmarks` | Full pipeline: resolve → generate → compile |
| `SqlSchemaProviderBenchmarks` | SQL Server schema introspection (requires Docker) |

## Commands

### List all benchmarks
```bash
./scripts/bench.sh
```

### Run benchmarks by filter
```bash
./scripts/bench.sh Compilation           # Roslyn compilation
./scripts/bench.sh CachedCompilation     # cache hit vs cold compile
./scripts/bench.sh EndToEnd              # full pipeline
./scripts/bench.sh CstParser             # template parsing
./scripts/bench.sh --all                 # everything (slow)
```

### Run with extra BenchmarkDotNet args
```bash
./scripts/bench.sh Compilation --iterationCount 10
./scripts/bench.sh Compilation --exporters json csv
```

### Collect a flamegraph (speedscope)
```bash
# Prerequisites: dotnet tool install -g dotnet-trace
./scripts/flamegraph.sh                                # default: DiffTest/SampleDb-Generator.csp
./scripts/flamegraph.sh path/to/your.csp               # specific file
./scripts/flamegraph.sh path/to/your.csp --no-cache    # bypass compilation cache
```
Output: `artifacts/traces/<name>-<timestamp>.speedscope.json`
Open with: `npx speedscope <file>` or upload to https://www.speedscope.app

### ETW profiler flamegraph (Windows, via BenchmarkDotNet)
```bash
./scripts/flamegraph-bench.sh Compilation
```
Output: `.etl` files in `BenchmarkDotNet.Artifacts/` — open with PerfView.

### Compare performance between git refs
```bash
./scripts/bench-compare.sh main HEAD Compilation
./scripts/bench-compare.sh abc123 def456 EndToEnd
```

## How to handle $ARGUMENTS

If the user provides arguments, interpret them as a benchmark filter or command:
- A benchmark name like "Compilation" or "EndToEnd" → run `./scripts/bench.sh $ARGUMENTS`
- "flamegraph" or "trace" → run `./scripts/flamegraph.sh`
- "compare <base> <head> <filter>" → run `./scripts/bench-compare.sh`
- No arguments → list available benchmarks and ask what to run

## Compilation cache

OpenSmith caches compiled template assemblies in the OS cache directory:
- Windows: `%LOCALAPPDATA%/opensmith/cache/`
- Linux: `$XDG_CACHE_HOME/opensmith/cache/` (or `~/.cache/opensmith/cache/`)
- macOS: `~/Library/Caches/opensmith/cache/`

Cache is keyed by SHA256 of all input sources. Pass `--no-cache` to the CLI to bypass.

## Adding new benchmarks

New benchmark classes go in `Test/OpenSmith.Benchmarks/Benchmarks/`. Use `BenchmarkTestContext.GetTemplatePath()` or `GetCspPath()` for file paths. Key patterns:
- `[MemoryDiagnoser]` on every class
- `[SimpleJob(RunStrategy.Monitoring, warmupCount:1, iterationCount:N)]` for slow benchmarks (>100ms)
- Use `[GlobalSetup]` to prepare inputs so the benchmark isolates the stage being measured
