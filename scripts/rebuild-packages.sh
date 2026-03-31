#!/usr/bin/env bash
# Rebuild all local NuGet packages and clear their cache entries.
#
# Usage:
#   ./scripts/rebuild-packages.sh

set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$REPO_ROOT/artifacts/packages"

CACHE_DIR="$(dotnet nuget locals global-packages -l | sed 's/.*: //')"

# Clear NuGet cache for any previously built packages
for nupkg in "$OUT"/*.nupkg; do
  [[ -f "$nupkg" ]] || continue
  filename="$(basename "$nupkg" .nupkg)"
  # Split "PackageId.1.2.3" into id and version.
  # sed strips from the first ".N." where N is all-digits (the version start).
  id="$(echo "$filename" | sed 's/\.\([0-9]\+\)\.\(.*\)$//')"
  version="${filename#"$id".}"
  pkg_cache="$CACHE_DIR/${id,,}/$version"

  if [[ -d "$pkg_cache" ]]; then
    echo "==> Clearing cache: $pkg_cache"
    rm -rf "$pkg_cache"
  fi
done

# Pack OpenSmith core packages
opensmith_projects=(
  opensmith/src/OpenSmith/OpenSmith.csproj
  opensmith/src/OpenSmith.Compilation/OpenSmith.Compilation.csproj
  opensmith/src/OpenSmith.SchemaExplorer/OpenSmith.SchemaExplorer.csproj
  opensmith/src/OpenSmith.Cli/OpenSmith.Cli.csproj
  opensmith/src/OpenSmith.Sdk.TemplatePackage/OpenSmith.Sdk.TemplatePackage.csproj
)

for proj in "${opensmith_projects[@]}"; do
  echo "==> Packing $proj"
  dotnet pack "$REPO_ROOT/$proj" -c Release -o "$OUT"
done

# Pack template packages (need UseLocalProjects=false to consume OpenSmith as NuGet packages)
template_projects=(
  plinqo/src/OpenSmith.Plinqo/OpenSmith.Plinqo.csproj
)

for proj in "${template_projects[@]}"; do
  echo "==> Packing $proj"
  dotnet pack "$REPO_ROOT/$proj" -c Release -o "$OUT" -p:UseLocalProjects=false
done

# Pack codesmith-data packages (only OpenSmith.Data.Lite is packable)
echo "==> Packing codesmith-data"
dotnet pack "$REPO_ROOT/codesmith-data/CodeSmith.Data.slnx" -c Release -o "$OUT"

echo "Done. Packages in $OUT:"
ls -1 "$OUT"/*.nupkg

# Clear OpenSmith runtime caches (dependency publish + template compilation)
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
  OPENSMITH_CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/opensmith"
elif [[ "$OSTYPE" == "darwin"* ]]; then
  OPENSMITH_CACHE="$HOME/Library/Caches/opensmith"
else
  OPENSMITH_CACHE="$LOCALAPPDATA/opensmith"
fi

if [[ -d "$OPENSMITH_CACHE" ]]; then
  echo "==> Clearing OpenSmith cache: $OPENSMITH_CACHE"
  rm -rf "$OPENSMITH_CACHE"
fi

# Reinstall the opensmith CLI local tool
echo "==> Reinstalling opensmith CLI tool"
cd "$REPO_ROOT"
dotnet tool uninstall opensmith.cli
dotnet tool install opensmith.cli
