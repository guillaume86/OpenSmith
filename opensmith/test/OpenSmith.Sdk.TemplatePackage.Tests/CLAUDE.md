# OpenSmith.Sdk.TemplatePackage.Tests

## Purpose

Integration tests for the `OpenSmith.Sdk.TemplatePackage` MSBuild SDK. They verify that template package authors can `dotnet pack` their templates into NuGet packages, and that consumers who reference those packages get the correct build-time behavior (template extraction, manifest generation, sample `.csp` file handling).

## How it works

Tests use a shared `TemplatePackageFixture` (xUnit collection fixture) that:

1. Packs the real `OpenSmith.Sdk.TemplatePackage` project into a local NuGet feed (temp directory).
2. Uses an isolated `NUGET_PACKAGES` cache to avoid polluting/depending on the user's global cache.
3. Provides helpers to scaffold throwaway template projects and consumer projects on disk.

Each test then exercises `dotnet pack` or `dotnet build` via `DotNetCliHelper` (a thin wrapper around `Process.Start`) and asserts on the resulting `.nupkg` contents or on-disk file layout.

### Key files

- **`TemplatePackageFixture.cs`** -- Shared fixture: packs the SDK, scaffolds projects, provides nupkg inspection helpers.
- **`DotNetCliHelper.cs`** -- Runs `dotnet` CLI commands as child processes with timeout support.
- **`PackTests.cs`** -- Asserts on nupkg contents produced by `dotnet pack` (template inclusion, targets/props injection, DLL bundling rules).
- **`ConsumerBuildTests.cs`** -- Asserts on the on-disk effects of `dotnet build` in a project that references a template package (template copying, manifest, idempotency).

## Running

```
dotnet test opensmith/test/OpenSmith.Sdk.TemplatePackage.Tests
```

Tests are slow (~minutes) because they invoke real `dotnet pack`/`build` processes. They require the .NET SDK to be installed.
