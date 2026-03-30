param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

# This repo currently has an MSBuild race when building project references in parallel.
# Single-node + non-parallel reference evaluation is stable.
dotnet build .\DeadVault.sln -m:1 -p:BuildInParallel=false -c $Configuration

