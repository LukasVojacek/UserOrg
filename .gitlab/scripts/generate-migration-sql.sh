#!/usr/bin/env bash
set -euo pipefail

# Generate the EF Core migration SQL script (migrations/sql/migration.sql) from the
# DatabaseMigrations tool. Prefers the binary produced by the build job; falls back to publishing.

export PATH="$HOME/.dotnet/tools:$PATH"
mkdir -p migrations/sql

# Ensure dotnet-ef is installed for the current user (idempotent).
echo "Ensuring dotnet-ef tool is installed..."
if ! dotnet tool list -g | grep -q '^dotnet-ef'; then
  dotnet tool install --global dotnet-ef --version 9.0.10 || true
else
  dotnet tool update --global dotnet-ef --version 9.0.10 || true
fi

# Prefer build artifacts produced by the userorg job (faster), fall back to a published binary.
BUILT_DLL=$(find src/Tools/DatabaseMigrations/bin/Release -type f -name 'DatabaseMigrations.dll' -print -quit 2>/dev/null || true)
PUBLISH_DIR=artifacts/bin/DatabaseMigrations/publish
if [ -n "$BUILT_DLL" ]; then
  DM_DLL="$BUILT_DLL"
else
  if [ -d "$PUBLISH_DIR" ] && [ "$(ls -A "$PUBLISH_DIR" 2>/dev/null)" ]; then
    echo "Using existing published output in $PUBLISH_DIR"
  else
    echo "Publishing DatabaseMigrations to $PUBLISH_DIR..."
    dotnet publish src/Tools/DatabaseMigrations -c Release -o "$PUBLISH_DIR" -p:Version=0.0.0
  fi
  DM_DLL="$PUBLISH_DIR/DatabaseMigrations.dll"
  if [ ! -f "$DM_DLL" ]; then
    DM_DLL=$(find "$PUBLISH_DIR" -maxdepth 3 -type f -name "DatabaseMigrations.dll" 2>/dev/null | head -n1 || true)
  fi
fi

if [ -z "$DM_DLL" ]; then
  echo "Failed to find DatabaseMigrations.dll"
  exit 1
fi

echo "Using DatabaseMigrations binary: $DM_DLL"
dotnet "$DM_DLL" script --output migrations/sql/migration.sql
