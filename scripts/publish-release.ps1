param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',
  [string]$Runtime = 'win-x64',
  [string]$OutputRoot = '.\artifacts\publish'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$uiOutput = Join-Path $OutputRoot 'DeadVault.UI'
$agentOutput = Join-Path $OutputRoot 'DeadVault.Agent'
$mcpOutput = Join-Path $OutputRoot 'DeadVault.McpServer'

dotnet publish .\src\DeadVault.UI\DeadVault.UI.csproj `
  -c $Configuration `
  -r $Runtime `
  --self-contained false `
  -o $uiOutput

dotnet publish .\src\DeadVault.Agent\DeadVault.Agent.csproj `
  -c $Configuration `
  -r $Runtime `
  --self-contained false `
  -o $agentOutput

dotnet publish .\src\DeadVault.McpServer\DeadVault.McpServer.csproj `
  -c $Configuration `
  -r $Runtime `
  --self-contained false `
  -o $mcpOutput

Write-Host "Published Desktop App to $uiOutput"
Write-Host "Published Agent to $agentOutput"
Write-Host "Published MCP Server to $mcpOutput"
