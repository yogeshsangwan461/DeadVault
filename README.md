# DeadVault

DeadVault is a local-first versioning layer for AI-assisted development. It watches one or more folders, snapshots changes into an internal Git-backed timeline, and gives you fast restore, diff, export, and attribution metadata without forcing the whole project to become your day-to-day Git repo.

## Why it matters

AI-assisted coding changes how version history feels:

- lots of small edits happen quickly
- you often want rollback without interrupting flow
- it becomes useful to mark whether a change was human- or AI-authored

DeadVault is a proof-of-concept aimed at that gap. It keeps an internal history, supports semantic version-style bumps, and now includes deterministic attribution watermark metadata plus an MCP server so tools like Claude can inspect versions and request rollback safely.

## What DeadVault does

- watches registered project folders with configurable debounce timing
- stores snapshots in a hidden `.deadvault` Git directory
- exposes timeline, diff, restore, export, and tagging in the WPF UI
- writes a tracked `.deadvault.attribution.json` manifest for deterministic text watermark metadata
- records per-snapshot attribution trailers so versions can be queried by `human` or `ai`
- ships an MCP server with:
  - `list_versions`
  - `get_diff`
  - `query_changes`
  - `rollback`

## Solution layout

- `src/DeadVault.UI`: tray app and WPF interface
- `src/DeadVault.Agent`: background watcher and IPC server
- `src/DeadVault.Core`: snapshot, diff, restore, export, watermark, and lock logic
- `src/DeadVault.Store`: JSON metadata persistence
- `src/DeadVault.McpServer`: MCP stdio server for AI tooling
- `tests/DeadVault.Tests`: lightweight regression tests

## Running locally

Requirements:

- .NET 9 SDK
- Windows

Build:

```powershell
.\scripts\build.ps1
```

Run the UI:

```powershell
dotnet run --project src\DeadVault.UI\DeadVault.UI.csproj
```

Run the agent directly:

```powershell
dotnet run --project src\DeadVault.Agent\DeadVault.Agent.csproj
```

## Attribution watermarking

DeadVault now includes a simple proof-of-concept attribution model:

- each project has a default author mode: `human`, `ai`, or `mixed`
- changed text files are fingerprinted deterministically into `.deadvault.attribution.json`
- snapshot commit messages include attribution trailers for fast querying
- the implementation is intentionally simple and capped by a per-snapshot text budget defaulting to 10 MB

This is not designed as production-grade provenance. It is a lightweight, inspectable demo mechanism.

## MCP integration

The MCP server uses the official C# MCP SDK and runs over stdio.

Build it:

```powershell
dotnet build src\DeadVault.McpServer\DeadVault.McpServer.csproj
```

Claude Desktop example:

```json
{
  "mcpServers": {
    "deadvault": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "Z:\\DeadVault\\src\\DeadVault.McpServer\\DeadVault.McpServer.csproj"
      ]
    }
  }
}
```

Available tools:

- `list_versions(projectId?, projectName?, limit?)`
- `get_diff(commitSha, projectId?, projectName?)`
- `query_changes(author, projectId?, projectName?, limit?)`
- `rollback(commitSha, projectId?, projectName?)`

## Notes for publishing

- the workspace is currently not a Git repo itself, so making it public on GitHub still needs:
  1. `git init`
  2. `git add .`
  3. `git commit -m "Initial public release"`
  4. create a GitHub repo and push

- runtime project metadata in `data/index.json` is machine-local state and should generally not be committed

## Verification

If `dotnet build DeadVault.sln` fails with a "0 errors" summary, use the stable build command:

```powershell
dotnet build DeadVault.sln -m:1 -p:BuildInParallel=false
```
