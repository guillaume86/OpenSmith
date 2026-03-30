#!/usr/bin/env bash
# End-to-end test: pack, install, and run OpenSmith + Plinqo from a fresh project.
#
# Verifies the full consumer workflow:
#   1. Pack local packages into a NuGet feed
#   2. Create a new dotnet project
#   3. Add OpenSmith.Plinqo package (templates + transitive deps)
#   4. Install OpenSmith.Cli as a dotnet tool
#   5. Spin up AdventureWorks in Docker
#   6. Run the full Dbml + Entities pipeline against the live database
#   7. Verify generated output
#
# Requires Docker to be running.
#
# Usage: ./scripts/e2e-test.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
WORK_DIR="$(mktemp -d)"
FEED_DIR="$WORK_DIR/packages"
PROJECT_DIR="$WORK_DIR/TestConsumer"
CONTAINER_NAME="opensmith-e2e-$$"
BAK_CACHE_DIR="${TMPDIR:-/tmp}/plinqo-test-data"
BAK_PATH="$BAK_CACHE_DIR/AdventureWorks2022.bak"
BAK_URL="https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorks2022.bak"
SA_PASSWORD="E2e_Test_Pwd!42"

cleanup() {
    echo ""
    echo "Cleaning up..."
    dotnet tool uninstall OpenSmith.Cli --tool-path "$WORK_DIR/tools" 2>/dev/null || true
    docker rm -f "$CONTAINER_NAME" 2>/dev/null || true
    rm -rf "$WORK_DIR"
}
trap cleanup EXIT

echo "=== E2E Test: OpenSmith + Plinqo consumer workflow ==="
echo "  Repo root : $REPO_ROOT"
echo "  Work dir  : $WORK_DIR"
echo ""

# -------------------------------------------------------
# Pre-check: Docker must be available
# -------------------------------------------------------
if ! command -v docker &>/dev/null; then
    echo "ERROR: docker is not installed or not in PATH."
    exit 1
fi

mkdir -p "$FEED_DIR"

# Clear cached e2e packages to avoid stale NuGet artifacts
dotnet nuget locals global-packages -l 2>/dev/null | grep -o '[^ ]*$' | while read -r cache_dir; do
    for pkg in opensmith opensmith.compilation opensmith.schemaexplorer opensmith.cli opensmith.plinqo; do
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

if [ -d "Templates/OpenSmith.Plinqo" ]; then
    TEMPLATE_COUNT=$(find Templates/OpenSmith.Plinqo -name "*.cst" | wc -l)
    echo "  Templates/OpenSmith.Plinqo/ found with $TEMPLATE_COUNT .cst files."
    TPL_PATH="Templates/OpenSmith.Plinqo"
else
    echo "  ERROR: Templates/OpenSmith.Plinqo/ not found after build."
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
# 5. Start SQL Server with AdventureWorks
# -------------------------------------------------------
echo "--- Step 5: Starting SQL Server with AdventureWorks ---"

# Download .bak if not cached
if [ ! -f "$BAK_PATH" ]; then
    echo "  Downloading AdventureWorks2022.bak (first run only, ~45 MB)..."
    mkdir -p "$BAK_CACHE_DIR"
    curl -fSL -o "$BAK_PATH.tmp" "$BAK_URL"
    mv "$BAK_PATH.tmp" "$BAK_PATH"
else
    echo "  Using cached AdventureWorks2022.bak"
fi

echo "  Starting SQL Server container..."
docker run -d --name "$CONTAINER_NAME" \
    -e "ACCEPT_EULA=Y" \
    -e "MSSQL_SA_PASSWORD=$SA_PASSWORD" \
    -p 0:1433 \
    mcr.microsoft.com/mssql/server:2022-latest > /dev/null

# Wait for SQL Server to become ready
echo "  Waiting for SQL Server to start..."
for i in $(seq 1 30); do
    if MSYS_NO_PATHCONV=1 docker exec "$CONTAINER_NAME" /opt/mssql-tools18/bin/sqlcmd \
        -S localhost -U sa -P "$SA_PASSWORD" -Q "SELECT 1" -C &>/dev/null; then
        break
    fi
    if [ "$i" -eq 30 ]; then
        echo "  ERROR: SQL Server did not start within 30 seconds."
        exit 1
    fi
    sleep 1
done

# Copy .bak into the container and restore
echo "  Restoring AdventureWorks database..."
# Create backup directory, copy .bak, and restore
MSYS_NO_PATHCONV=1 docker exec "$CONTAINER_NAME" mkdir -p /var/opt/mssql/backup
BAK_PATH_WIN="$(cd "$(dirname "$BAK_PATH")" && pwd -W 2>/dev/null || pwd)/$(basename "$BAK_PATH")"
MSYS_NO_PATHCONV=1 docker cp "$BAK_PATH_WIN" "$CONTAINER_NAME:/var/opt/mssql/backup/AdventureWorks2022.bak"
MSYS_NO_PATHCONV=1 docker exec "$CONTAINER_NAME" /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U sa -P "$SA_PASSWORD" -C -Q "
    RESTORE DATABASE [AdventureWorks2022]
    FROM DISK = '/var/opt/mssql/backup/AdventureWorks2022.bak'
    WITH MOVE 'AdventureWorks2022' TO '/var/opt/mssql/data/AdventureWorks2022.mdf',
         MOVE 'AdventureWorks2022_log' TO '/var/opt/mssql/data/AdventureWorks2022_log.ldf'
" > /dev/null

# Resolve the mapped host port
HOST_PORT=$(docker port "$CONTAINER_NAME" 1433/tcp | head -1 | cut -d: -f2)
CONN_STRING="Data Source=localhost,$HOST_PORT;Initial Catalog=AdventureWorks2022;User ID=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
echo "  AdventureWorks ready on port $HOST_PORT."
echo ""

# -------------------------------------------------------
# 6. Set up CSP and run the full pipeline
# -------------------------------------------------------
echo "--- Step 6: Setting up and running the full pipeline ---"

mkdir -p Generated/Entities

# Create a CSP that runs both Dbml generation (from live DB) and Entities generation.
# This exercises the full dependency chain:
#   - Dbml.cst → SchemaExplorer (SQL introspection) + Generator (DBML creation)
#   - Entities.cst → Dbml (DBML model) + Generator (code gen helpers)
cat > Generate.csp << CSPEOF
<?xml version="1.0" encoding="utf-8" ?>
<codeSmith xmlns="http://www.codesmithtools.com/schema/csp.xsd">
  <variables>
    <add key="ConnectionString1" value="$CONN_STRING" />
  </variables>
  <propertySets>
    <propertySet name="Dbml" output="./Generated/AdventureWorks.dbml" template="$TPL_PATH/Dbml.cst">
      <property name="IncludeViews">True</property>
      <property name="IncludeFunctions">True</property>
      <property name="IgnoreList">
        <stringList>
          <string>sysdiagrams\$</string>
        </stringList>
      </property>
      <property name="CleanExpression">
        <stringList>
          <string>^(sp|tbl|udf|vw)_</string>
        </stringList>
      </property>
      <property name="EntityBase">LinqEntityBase</property>
      <property name="IncludeDeleteOnNull">True</property>
      <property name="NamingConventions">
        <NamingProperty xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
          xmlns:xsd="http://www.w3.org/2001/XMLSchema"
          xmlns="">
          <TableNaming>Mixed</TableNaming>
          <EntityNaming>Singular</EntityNaming>
          <AssociationNaming>ListSuffix</AssociationNaming>
        </NamingProperty>
      </property>
      <property name="SourceDatabase">
        <connectionString>\$(ConnectionString1)</connectionString>
        <providerType>SchemaExplorer.SqlSchemaProvider,OpenSmith.SchemaExplorer</providerType>
      </property>
      <property name="EntityNamespace">AdventureWorks.Data</property>
      <property name="ContextNamespace">AdventureWorks.Data</property>
      <property name="DbmlFile">Generated/AdventureWorks.dbml</property>
    </propertySet>
    <propertySet name="Entities" template="$TPL_PATH/Entities.cst">
      <property name="DbmlFile">./Generated/AdventureWorks.dbml</property>
      <property name="Framework">v45</property>
      <property name="IncludeDataServices">False</property>
      <property name="IncludeDataRules">False</property>
      <property name="AuditingEnabled">False</property>
      <property name="IncludeDataContract">True</property>
      <property name="IncludeXmlSerialization">False</property>
      <property name="IncludeManyToMany">False</property>
      <property name="AssociationNamingSuffix">ListSuffix</property>
      <property name="OutputDirectory">./Generated/Entities</property>
      <property name="BaseDirectory">./Generated/</property>
      <property name="InterfaceDirectory" />
    </propertySet>
  </propertySets>
</codeSmith>
CSPEOF

echo "  Running full pipeline: Dbml (SQL introspection) -> Entities (code gen)..."
echo "  If any transitive dependency is missing, Roslyn compilation will fail."
echo ""

"$OPENSMITH" Generate.csp --verbose --no-cache
echo ""

# -------------------------------------------------------
# 7. Verify output
# -------------------------------------------------------
echo "--- Step 7: Verifying output ---"

DBML_EXISTS="no"
if [ -f "Generated/AdventureWorks.dbml" ]; then
    DBML_EXISTS="yes"
    DBML_SIZE=$(wc -c < "Generated/AdventureWorks.dbml")
    echo "  DBML file        : Generated/AdventureWorks.dbml ($DBML_SIZE bytes)"
fi

ENTITY_COUNT=$(find Generated/Entities -name "*.cs" 2>/dev/null | wc -l)
CONTEXT_COUNT=$(find Generated -maxdepth 1 -name "*DataContext*" 2>/dev/null | wc -l)
BASE_COUNT=$(find Generated -maxdepth 1 -name "*EntityBase*" 2>/dev/null | wc -l)

echo "  Entity files     : $ENTITY_COUNT"
echo "  DataContext files : $CONTEXT_COUNT"
echo "  EntityBase files  : $BASE_COUNT"

TOTAL=$((ENTITY_COUNT + CONTEXT_COUNT + BASE_COUNT))

if [ "$DBML_EXISTS" = "yes" ] && [ "$TOTAL" -gt 0 ]; then
    echo ""
    echo "  Sample generated files:"
    find Generated -name "*.cs" | head -5 | sed 's/^/    /' || true
    echo ""
    echo "=== E2E TEST PASSED ==="
    echo "  Generated DBML + $TOTAL C# files from a fresh consumer project."
    echo "  Full pipeline (SQL introspection -> DBML -> code gen) verified."
    echo "  Transitive dependencies (Dbml, Generator, SchemaExplorer) resolved correctly."
    exit 0
else
    echo ""
    echo "=== E2E TEST FAILED ==="
    echo "  DBML generated: $DBML_EXISTS"
    echo "  C# files generated: $TOTAL"
    find Generated -type f 2>/dev/null | sed 's/^/    /' || echo "    (empty)"
    exit 1
fi
