# OpenSmith

A minimal .NET reimplementation of the [CodeSmith Generator](https://www.codesmithtools.com/) template engine and [PLINQO](https://github.com/codesmithtools/Templates) (Professional LINQ to Objects) code generation templates.

The original CodeSmith Generator is a proprietary, Windows-only code generation tool. This project extracts and modernises the core functionality into standalone .NET 10 libraries — no CodeSmith installation required.

## Solutions

The repository is organised into three independent solutions (plus a root roll-up):

| Solution             | Path                                 | Description                                                                         |
| -------------------- | ------------------------------------ | ----------------------------------------------------------------------------------- |
| **OpenSmith**        | [`opensmith/`](opensmith/)           | Template engine, Roslyn-based compiler, and SQL Server schema explorer              |
| **OpenSmith.Plinqo** | [`plinqo/`](plinqo/)                 | DBML object model, database-to-DBML generator, and PLINQO code generation templates |
| **CodeSmith.Data**   | [`codesmith-data/`](codesmith-data/) | Legacy data access library used by generated LINQ to SQL code                       |
| **OpenSmith.All**    | `OpenSmith.All.slnx`                 | Root solution containing all projects                                               |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- SQL Server instance (for schema exploration and code generation)

### Build

```bash
# Build everything
dotnet build OpenSmith.All.slnx

# Or build a single solution
dotnet build opensmith/OpenSmith.slnx
dotnet build plinqo/OpenSmith.Plinqo.slnx
dotnet build codesmith-data/CodeSmith.Data.slnx
```

### Run Tests

```bash
dotnet test OpenSmith.All.slnx
```

Tests that hit SQL Server use [Testcontainers](https://dotnet.testcontainers.org/) to spin up a disposable Docker container automatically.

## Usage

### 1. Add the packages

Add the PLINQO templates and runtime library to your project:

```bash
dotnet add package OpenSmith.Plinqo
dotnet add package CodeSmith.Data.LinqToSql
```

On first build, the PLINQO NuGet package copies a set of `.cst` templates and a sample `.csp` project file into your project directory.

### 2. Configure the CSP file

The `.csp` file is an XML project file that drives code generation. Edit the sample to point at your database:

```xml
<codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
  <variables>
    <add key="ConnectionString1"
         value="Data Source=.\SQLEXPRESS;Initial Catalog=MyDatabase;Integrated Security=True;TrustServerCertificate=True" />
  </variables>
  <propertySets>
    <!-- Phase 1: introspect the database and produce a DBML mapping file -->
    <propertySet name="Dbml" output=".\Generated\Dbml.xml" template="Templates\Dbml.cst">
      <property name="SourceDatabase">
        <connectionString>$(ConnectionString1)</connectionString>
        <providerType>SchemaExplorer.SqlSchemaProvider,OpenSmith.SchemaExplorer</providerType>
      </property>
      <property name="EntityNamespace">MyProject.Data</property>
      <property name="ContextNamespace">MyProject.Data</property>
      <property name="DbmlFile">Generated\MyDatabase.dbml</property>
      <!-- ... naming conventions, ignore lists, etc. -->
    </propertySet>

    <!-- Phase 2: generate C# entities, data context, and managers from the DBML -->
    <propertySet name="Entities" template="Templates\Entities.cst">
      <property name="DbmlFile">.\Generated\MyDatabase.dbml</property>
      <property name="OutputDirectory">.\Generated\Entities</property>
      <property name="BaseDirectory">.\Generated\</property>
      <!-- ... generation options -->
    </propertySet>
  </propertySets>
</codeSmith>
```

See [`plinqo/src/OpenSmith.Plinqo/Templates/Sample-Generator.csp`](plinqo/src/OpenSmith.Plinqo/Templates/Sample-Generator.csp) for a complete example with all available properties.

### 3. Run the generator

```bash
opensmith MyProject/Generator.csp [--verbose] [--no-cache]
```

The CLI processes each `<propertySet>` in order:

1. **Dbml** — connects to SQL Server, reads the schema, and writes a `.dbml` mapping file
2. **Entities** — reads the `.dbml` and generates C# source files

### 4. Generated output

```
Generated/
├── MyDatabase.dbml              # Database schema mapping (XML)
├── DataContext.Generated.cs     # Regenerated every run
├── DataContext.Editable.cs      # Created once — safe to edit
└── Entities/
    ├── Customer.Generated.cs    # Regenerated every run
    ├── Customer.Editable.cs     # Created once — safe to edit
    └── ...
```

Files ending in `.Generated.cs` are fully overwritten on each run. `.Editable.cs` files are only created if they don't already exist, so your customisations are preserved.

### 5. Iterate

When the database schema changes, re-run `opensmith` to regenerate. Your editable partial classes remain untouched.

## License

This project is licensed under the [Apache License 2.0](LICENSE).

It is a derivative work of the [codesmithtools/Templates](https://github.com/codesmithtools/Templates) repository (also Apache-2.0), which contains the original PLINQO templates and supporting libraries. The CodeSmith-specific runtime has been replaced with a lightweight, self-contained engine built on Roslyn.
