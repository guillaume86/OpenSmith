# OpenSmith.Sdk.TemplatePackage

## Purpose

An MSBuild SDK distributed as a NuGet package. Template package authors reference it to get automatic packing and consumer-side extraction of CodeSmith-style `.cst` templates.

It has **no runtime code** (`IncludeBuildOutput=false`). The entire package is MSBuild `.props`, `.targets`, and a generic consumer targets file shipped inside `build/`.

## How it works

### Author side (pack-time)

When a template package author adds `<PackageReference Include="OpenSmith.Sdk.TemplatePackage" PrivateAssets="all" />`, the SDK's [OpenSmith.Sdk.TemplatePackage.targets](build/OpenSmith.Sdk.TemplatePackage.targets) is auto-imported and does the following at `dotnet pack`:

1. **Packs templates** -- all files under `Templates/` go into the nupkg at `templates/`.
2. **Excludes templates from compilation** -- `Templates/**` is removed from the `Compile` item group.
3. **Injects consumer targets** -- if the author provides a custom `build/{PackageId}.targets`, it's packed as-is. Otherwise, the generic [OpenSmith.TemplatePackage.Consumer.targets](build/OpenSmith.TemplatePackage.Consumer.targets) is packed and renamed to `build/{PackageId}.targets`.
4. **Custom `.props`** -- if `build/{PackageId}.props` exists, it's packed too.

### Consumer side (build-time)

When a consumer project references the resulting template NuGet package, the `build/{PackageId}.targets` (generic or custom) is auto-imported by NuGet. The generic consumer targets:

1. **`_OpenSmithCopyTemplates`** (BeforeBuild) -- copies templates from the NuGet cache into `Templates/{PackageId}/` in the consumer project. Skipped if the destination directory already exists (preserves user edits). Writes a `.opensmith.json` manifest with the package name and version.
2. **`_OpenSmithCopySampleCsp`** (BeforeBuild) -- copies `Sample-Generator.csp` to the consumer project root, if present in the package and not already on disk.

### Key files

- **`OpenSmith.Sdk.TemplatePackage.csproj`** -- Package definition. No build output, ships only MSBuild files.
- **`build/OpenSmith.Sdk.TemplatePackage.props`** -- Marks the SDK as a `DevelopmentDependency`.
- **`build/OpenSmith.Sdk.TemplatePackage.targets`** -- Author-side logic: template packing, compilation exclusion, consumer targets injection.
- **`build/OpenSmith.TemplatePackage.Consumer.targets`** -- Generic consumer-side targets: template copy, manifest write, sample CSP copy.
