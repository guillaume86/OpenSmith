#!/usr/bin/env bash
# End-to-end test: pack, install, and run OpenSmith + Plinqo from a fresh project.
#
# Verifies the full consumer workflow:
#   1. Pack local packages into a NuGet feed
#   2. Create a new dotnet project
#   3. Add OpenSmith.Plinqo package (templates + transitive deps)
#   4. Install OpenSmith.Cli as a dotnet tool
#   5. Run templates that depend on transitive assemblies (Dbml, Generator, SchemaExplorer)
#   6. Verify generated output
#
# Does NOT require a SQL Server instance — uses a pre-existing .dbml file.
#
# Usage: ./scripts/e2e-test.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
WORK_DIR="$(mktemp -d)"
FEED_DIR="$WORK_DIR/packages"
PROJECT_DIR="$WORK_DIR/TestConsumer"

cleanup() {
    echo ""
    echo "Cleaning up $WORK_DIR ..."
    dotnet tool uninstall OpenSmith.Cli --tool-path "$WORK_DIR/tools" 2>/dev/null || true
    rm -rf "$WORK_DIR"
}
trap cleanup EXIT

echo "=== E2E Test: OpenSmith + Plinqo consumer workflow ==="
echo "  Repo root : $REPO_ROOT"
echo "  Work dir  : $WORK_DIR"
echo ""

mkdir -p "$FEED_DIR"

# Clear cached e2e packages to avoid stale NuGet artifacts
dotnet nuget locals global-packages -l 2>/dev/null | grep -o '[^ ]*$' | while read -r cache_dir; do
    for pkg in opensmith opensmith.compilation opensmith.schemaexplorer opensmith.cli opensmith.plinqo dbml generator; do
        rm -rf "$cache_dir/$pkg/0.0.1-e2etest" 2>/dev/null
    done
done

# -------------------------------------------------------
# 1. Pack all required packages into local feed
# -------------------------------------------------------
echo "--- Step 1: Packing NuGet packages ---"

PACK_VERSION="0.0.1-e2etest"

for proj in \
    opensmith/src/OpenSmith/OpenSmith.csproj \
    opensmith/src/OpenSmith.Compilation/OpenSmith.Compilation.csproj \
    opensmith/src/OpenSmith.SchemaExplorer/OpenSmith.SchemaExplorer.csproj \
    opensmith/src/OpenSmith.Cli/OpenSmith.Cli.csproj \
    plinqo/src/Dbml/Dbml.csproj \
    plinqo/src/Generator/Generator.csproj \
    plinqo/src/OpenSmith.Plinqo/OpenSmith.Plinqo.csproj; do
    echo "  Packing $(basename "$(dirname "$proj")") ..."
    dotnet pack "$REPO_ROOT/$proj" -o "$FEED_DIR" --configuration Release \
        -p:Version="$PACK_VERSION" > /dev/null 2>&1
done

echo "  Packages:"
ls "$FEED_DIR"/*.nupkg | xargs -I{} basename {} | sed 's/^/    /'
echo ""

# -------------------------------------------------------
# 2. Create a fresh consumer project
# -------------------------------------------------------
echo "--- Step 2: Creating consumer project ---"

dotnet new console -o "$PROJECT_DIR" --no-restore > /dev/null 2>&1

cat > "$PROJECT_DIR/nuget.config" << 'NUGETEOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-e2e" value="../packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-e2e">
      <package pattern="*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
NUGETEOF

echo "  Created project at $PROJECT_DIR"
echo ""

# -------------------------------------------------------
# 3. Add the OpenSmith.Plinqo package
# -------------------------------------------------------
echo "--- Step 3: Adding OpenSmith.Plinqo package ---"

cd "$PROJECT_DIR"
dotnet add package OpenSmith.Plinqo --version "$PACK_VERSION" > /dev/null 2>&1
echo "  Package added."

# The .targets file copies templates on first build
echo "  Building to trigger template copy..."
dotnet build > /dev/null 2>&1

if [ -d "Templates/Plinqo" ]; then
    TEMPLATE_COUNT=$(find Templates/Plinqo -name "*.cst" | wc -l)
    echo "  Templates/Plinqo/ found with $TEMPLATE_COUNT .cst files."
    TPL_PATH="Templates/Plinqo"
else
    echo "  ERROR: Templates/Plinqo/ not found after build."
    echo "  .targets file did not copy templates."
    exit 1
fi

if [ -f "Sample-Generator.csp" ]; then
    echo "  Sample-Generator.csp copied."
else
    echo "  WARNING: Sample-Generator.csp not copied."
fi
echo ""

# -------------------------------------------------------
# 4. Install OpenSmith CLI as a local tool
# -------------------------------------------------------
echo "--- Step 4: Installing OpenSmith CLI tool ---"

dotnet tool install OpenSmith.Cli --version "$PACK_VERSION" --tool-path "$WORK_DIR/tools" \
    --add-source "$FEED_DIR" > /dev/null 2>&1
OPENSMITH="$WORK_DIR/tools/opensmith"
echo "  Installed opensmith CLI."
echo ""

# -------------------------------------------------------
# 5. Set up test scenario
# -------------------------------------------------------
echo "--- Step 5: Setting up test scenario ---"

# Copy a test .dbml (no database needed)
mkdir -p Generated/Entities
cp "$REPO_ROOT/plinqo/DiffTest/Expected/SampleDb.dbml" Generated/SampleDb.dbml

# Create a CSP that exercises transitive dependency resolution:
#   - Entities.cst imports LinqToSqlShared.DbmlObjectModel (from Dbml)
#   - Entities.cst imports LinqToSqlShared.Generator (from Generator)
#   - Sub-templates import SchemaExplorer (from OpenSmith.SchemaExplorer)
# If any transitive assembly is missing, the Roslyn compilation will fail.
cat > Generate.csp << CSPEOF
<?xml version="1.0" encoding="utf-8" ?>
<codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
  <variables />
  <propertySets>
    <propertySet name="Entities" template="$TPL_PATH/Entities.cst">
      <property name="DbmlFile">.\Generated\SampleDb.dbml</property>
      <property name="Framework">v45</property>
      <property name="IncludeDataServices">False</property>
      <property name="IncludeDataRules">False</property>
      <property name="AuditingEnabled">False</property>
      <property name="IncludeDataContract">True</property>
      <property name="IncludeXmlSerialization">False</property>
      <property name="IncludeManyToMany">False</property>
      <property name="AssociationNamingSuffix">ListSuffix</property>
      <property name="OutputDirectory">.\Generated\Entities</property>
      <property name="BaseDirectory">.\Generated\</property>
      <property name="InterfaceDirectory" />
    </propertySet>
  </propertySets>
</codeSmith>
CSPEOF

echo "  CSP file created. Templates from: $TPL_PATH"
echo ""

# -------------------------------------------------------
# 6. Run the CLI
# -------------------------------------------------------
echo "--- Step 6: Running opensmith ---"
echo "  This compiles 9 templates that import Dbml, Generator, and SchemaExplorer."
echo "  If any transitive dependency is missing, compilation will fail."
echo ""

"$OPENSMITH" Generate.csp --verbose --no-cache
echo ""

# -------------------------------------------------------
# 7. Verify output
# -------------------------------------------------------
echo "--- Step 7: Verifying output ---"

ENTITY_COUNT=$(find Generated/Entities -name "*.cs" 2>/dev/null | wc -l)
CONTEXT_COUNT=$(find Generated -maxdepth 1 -name "*DataContext*" 2>/dev/null | wc -l)
BASE_COUNT=$(find Generated -maxdepth 1 -name "*EntityBase*" 2>/dev/null | wc -l)

echo "  Entity files     : $ENTITY_COUNT"
echo "  DataContext files : $CONTEXT_COUNT"
echo "  EntityBase files  : $BASE_COUNT"

TOTAL=$((ENTITY_COUNT + CONTEXT_COUNT + BASE_COUNT))

if [ "$TOTAL" -gt 0 ]; then
    echo ""
    echo "  Sample generated files:"
    find Generated -name "*.cs" | head -5 | sed 's/^/    /'
    echo ""
    echo "=== E2E TEST PASSED ==="
    echo "  Generated $TOTAL files from a fresh consumer project."
    echo "  Transitive dependencies (Dbml, Generator, SchemaExplorer) resolved correctly."
    exit 0
else
    echo ""
    echo "=== E2E TEST FAILED ==="
    echo "  No files were generated."
    find Generated -type f 2>/dev/null | sed 's/^/    /' || echo "    (empty)"
    exit 1
fi
