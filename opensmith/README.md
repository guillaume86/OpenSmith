# OpenSmith

A minimal .NET reimplementation of the CodeSmith Generator template engine. Parses and compiles CodeSmith template files (`.cst`) and project files (`.csp`) using Roslyn, with no dependency on the original CodeSmith installation.

## Projects

| Project | Description |
|---------|-------------|
| **OpenSmith** | Template engine base classes (`CodeTemplateBase`, `ResponseWriter`, etc.) that provide the API surface CodeSmith templates expect at runtime |
| **OpenSmith.Compilation** | CSP/CST parser, Roslyn-based template compiler, template registry for dependency resolution, and SHA256-based disk cache |
| **OpenSmith.SchemaExplorer** | SQL Server schema exploration — queries tables, views, stored procedures, functions, columns, indexes, and constraints via `Microsoft.Data.SqlClient` |
| **OpenSmith.Cli** | Command-line tool that orchestrates the full pipeline: parse a `.csp` file, resolve templates, compile, and execute |
| **OpenSmith.Sdk.TemplatePackage** | MSBuild SDK that packages `.cst` templates as NuGet packages and copies them into consuming projects on first build |

## Architecture

```
CSP file ──► CspParser ──► TemplateRegistry ──► TemplateCodeGenerator ──► TemplateCompiler (Roslyn) ──► Execute
                                                                                   │
                                                                          TemplateCompilationCache
```

1. **CspParser** reads the XML-based `.csp` project file
2. **TemplateRegistry** resolves the template dependency graph
3. **TemplateCodeGenerator** generates C# source from `.cst` template directives
4. **TemplateCompiler** compiles the generated source to an in-memory assembly via Roslyn
5. **CspRunner** orchestrates the full workflow

## CLI Usage

```bash
opensmith <path-to-project.csp> [--verbose] [--no-cache] [--clear-cache]
```

## Tests

| Project | Covers |
|---------|--------|
| **OpenSmith.Tests** | Engine utilities, template parsing, merge strategies, SQL schema provider (integration via Testcontainers) |
| **OpenSmith.Compilation.Tests** | CSP parsing, property deserialisation, code generation, compilation, template registry |
| **OpenSmith.Sdk.TemplatePackage.Tests** | Template package SDK integration tests |
| **OpenSmith.Benchmarks** | BenchmarkDotNet performance tests for every pipeline stage |
