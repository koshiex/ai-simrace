#!/usr/bin/env bash
# Bootstrap the .NET solution. Run from repo root.
set -euo pipefail

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found. Install .NET 9 SDK first."
  exit 1
fi

SLN=SimCoach.sln

if [[ -f "$SLN" ]]; then
  echo "Solution already exists. Skipping create."
else
  # --format sln: SDK 10+ creates .slnx by default; fall back for SDKs without the option.
  dotnet new sln -n SimCoach --format sln || dotnet new sln -n SimCoach
fi

# Add all src projects.
for csproj in src/*/*.csproj; do
  dotnet sln "$SLN" add "$csproj"
done

# Add all test projects.
for csproj in tests/*/*.csproj; do
  dotnet sln "$SLN" add "$csproj"
done

# Add all tool projects (offline dev tools, e.g. SimCoach.Bake). Guarded so an empty glob is a no-op.
for csproj in tools/*/*.csproj; do
  [[ -e "$csproj" ]] || continue
  dotnet sln "$SLN" add "$csproj"
done

# Restore.
dotnet restore "$SLN"

echo ""
echo "Bootstrap complete."
echo "Next: dotnet build  (and on Windows, dotnet run --project src/SimCoach.App)"
