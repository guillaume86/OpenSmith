# OpenSmith.Plinqo

PLINQO (Professional LINQ to Objects) code generation for .NET — a minimal reimplementation of the [original PLINQO templates](https://github.com/codesmithtools/Templates) from CodeSmith Generator. Generates LINQ to SQL entities, data contexts, and managers from a SQL Server database.

## Projects

| Project | Description |
|---------|-------------|
| **Dbml** | Complete object model for DBML (Database Markup Language) XML files — tables, columns, associations, functions, and more. Includes `DbmlReader`, `DbmlSerializer`, `DbmlVisitor`, and `DbmlDuplicator` |
| **Generator** | Converts a live SQL Server schema (via `OpenSmith.SchemaExplorer`) into the DBML object model. Handles naming conventions, ignore/include patterns, and enum mappings |
| **OpenSmith.Plinqo** | The PLINQO templates themselves (18 `.cst` files + sample `.csp`), packaged as a NuGet `DevelopmentDependency`. Includes MSBuild targets that copy templates into the consuming project on first build |

## Templates

```
Templates/
├── Dbml.cst                          # Generate DBML from database
├── Entities.cst                      # Main entity generation entry point
├── Managers.cst                      # Entity manager generation
├── Queries.cst                       # Query extension generation
├── Sample-Generator.csp              # Sample CSP project file
└── Internal/
    ├── DataContext.Generated.cst      # Generated data context
    ├── DataContext.Editable.cst       # User-editable data context
    ├── Entity.Generated.cst           # Generated entity classes
    ├── Entity.Editable.cst            # User-editable entity partial classes
    ├── Entity.Base.Generated.cst      # Base entity templates
    ├── Entity.Base.Editable.cst       # User-editable base entity partial classes
    ├── Entity.Interface.cst           # Entity interfaces
    ├── EntityManager.Generated.cst    # Generated managers
    ├── EntityManager.Editable.cst     # User-editable managers
    ├── DataContext.Manager.cst         # Data context manager
    ├── DataManager.cst                 # Data manager base
    ├── Enums.cst                      # Enum generation
    ├── QueryExtension.Generated.cst   # Generated query extensions
    └── QueryExtension.Editable.cst    # User-editable query extensions
```

Generated files are fully regenerated on each run. Editable files are only created if they don't already exist, preserving user customisations.

## Tests

**OpenSmith.Plinqo.Tests** covers the DBML generator (all-tables, ignore-column, delete-on-null, baseline comparisons) and end-to-end code generation, using the AdventureWorks sample database via Testcontainers.
