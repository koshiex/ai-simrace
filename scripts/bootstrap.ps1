#!/usr/bin/env pwsh
# Bootstrap the .NET solution. Run from repo root on Windows.
$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK not found. Install .NET 9 SDK first."
    exit 1
}

$sln = "SimCoach.sln"

if (Test-Path $sln) {
    Write-Host "Solution already exists. Skipping create."
} else {
    # --format sln: SDK 10+ creates .slnx by default; fall back for SDKs without the option.
    dotnet new sln -n SimCoach --format sln
    if ($LASTEXITCODE -ne 0) { dotnet new sln -n SimCoach }
}

# Add all src projects.
Get-ChildItem -Path "src" -Filter "*.csproj" -Recurse | ForEach-Object {
    dotnet sln $sln add $_.FullName
}

# Add all test projects.
Get-ChildItem -Path "tests" -Filter "*.csproj" -Recurse | ForEach-Object {
    dotnet sln $sln add $_.FullName
}

# Restore.
dotnet restore $sln

Write-Host ""
Write-Host "Bootstrap complete."
Write-Host "Next: dotnet build  (and dotnet run --project src/SimCoach.App)"
