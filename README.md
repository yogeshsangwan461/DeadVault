# DeadVault

DeadVault is a Windows desktop app for local-first version history during AI-assisted development. It watches one or more folders, snapshots changes into an internal Git-backed timeline, and gives you fast restore, diff, export, tags, and attribution metadata without forcing the project itself to become your working Git repo.

This repo ships two distinct surfaces:

- `DeadVault.UI` + `DeadVault.Agent`: the desktop app and background watcher you use locally
- `DeadVault.McpServer`: the MCP stdio server that lets AI clients inspect versions, diff changes, and create tagged versions

## Highlights

- local snapshot timeline for any registered folder
- one-click restore with automatic pre-restore backup
- diff, export, tagging, and semantic version bump support
- fast local project summaries and timeline browsing
- deterministic attribution manifest for changed text files
- MCP version creation with explicit AI/client metadata
- separate publish outputs for desktop app, agent, and MCP server

## What ships

### Desktop App

The desktop app is the main product. It is made of:

- `DeadVault.UI`: the WPF interface
- `DeadVault.Agent`: the background watcher and IPC service

Use it when you want:

- automatic local snapshots while you work
- manual `Create Version` bumps from the UI
- restore, diff, tag, and export flows
- a Windows app you can package into a GitHub release ZIP

### MCP Server

The MCP server is for AI tooling, not for end users browsing the UI. It exposes DeadVault over stdio so tools like Claude Desktop can:

- list registered projects
- register a new project folder
- list versions
- inspect diffs
- query changes by attribution
- roll back to a prior version
- create versions with AI/client/model-family metadata

Use it when you want AI-generated changes to identify themselves through DeadVault instead of relying on the desktop app UI.

## How it works

- watches registered project folders with configurable debounce timing
- stores snapshots in a hidden `.deadvault` Git directory inside each tracked folder
- exposes timeline, diff, restore, export, and tagging in the WPF UI
- defaults local app snapshots to `human`
- lets MCP clients override attribution with `authorKind`, `clientName`, `modelFamily`, and `model`
- records attribution trailers on snapshots for later querying
- writes a tracked `.deadvault.attribution.json` manifest for deterministic text provenance metadata

## Solution layout

- `src/DeadVault.UI`: tray app and WPF interface
- `src/DeadVault.Agent`: background watcher and IPC server
- `src/DeadVault.Core`: snapshot, diff, restore, export, attribution, and lock logic
- `src/DeadVault.Store`: JSON metadata persistence
- `src/DeadVault.McpServer`: MCP stdio server for AI tooling
- `tests/DeadVault.Tests`: lightweight regression tests

## Requirements

- .NET 9 SDK
- Windows

## Build and run

Build the full solution:

```powershell
.\scripts\build.ps1
```

Run the UI:

```powershell
dotnet run --project src\DeadVault.UI\DeadVault.UI.csproj
```

Run the background agent directly:

```powershell
dotnet run --project src\DeadVault.Agent\DeadVault.Agent.csproj
```

Run the MCP server directly:

```powershell
dotnet run --project src\DeadVault.McpServer\DeadVault.McpServer.csproj
```

## Publish outputs

For GitHub releases or manual ZIP distribution, publish all runtime surfaces:

```powershell
.\scripts\publish-release.ps1
```

This creates:

- `artifacts\publish\DeadVault.UI`
- `artifacts\publish\DeadVault.Agent`
- `artifacts\publish\DeadVault.McpServer`

If you want a downloadable desktop build, package the `DeadVault.UI` and `DeadVault.Agent` outputs together in the same release.

## Quick start

1. Open DeadVault and click `Add Project`.
2. Pick the folder you want DeadVault to watch.
3. Make edits normally, or click `Create Version` to snapshot immediately.
4. Open `Timeline` to diff, restore, export, or tag any saved version.
5. Open `Settings` to tune debounce, attribution manifest behavior, and agent startup behavior.

## Attribution

- local app snapshots default to `human`
- MCP-created versions can identify `authorKind`, `clientName`, `modelFamily`, and `model`
- changed text files are fingerprinted deterministically into `.deadvault.attribution.json`
- snapshot commit messages include attribution trailers for fast querying
- the implementation is intentionally simple and capped by a per-snapshot text budget defaulting to 10 MB

This feature is lightweight and inspectable by design. It is useful for local provenance tracking, but it is not a tamper-proof authorship guarantee.

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
        "C:\\path\\to\\DeadVault\\src\\DeadVault.McpServer\\DeadVault.McpServer.csproj"
      ]
    }
  }
}
```

Available tools:

- `list_projects()`
- `register_project(folderPath, projectName, createFolderIfMissing, activate, debounceSeconds, enableAttributionManifest, watermarkingSource, reloadAgent)`
- `list_versions(projectId?, projectName?, limit?)`
- `get_diff(commitSha, projectId?, projectName?)`
- `query_changes(author, projectId?, projectName?, limit?)`
- `rollback(commitSha, projectId?, projectName?)`
- `create_version(bumpKind, customVersion, note, authorKind, model, modelFamily, clientName, clientTimestampUtc, projectId, projectName)`

## GitHub publishing

1. Create a new GitHub repository.
2. Add the remote:

```powershell
git remote add origin <url>
```

3. Push the current branch:

```powershell
git push -u origin main
```

Machine-local runtime metadata in `data/index.json` should usually stay out of source control.

## Roadmap

### v1.1 — Connectors
- VS Code extension: timeline sidebar, one-click restore, inline attribution blame
- Cursor integration: native DeadVault history panel inside Cursor
- CLI tool: `dvault snapshot`, `dvault restore`, `dvault diff` from terminal
- File count display fix in timeline (currently shows -1 pending UI optimization)

### v1.2 — Attribution Intelligence
- Automatic human/AI detection based on file change patterns (no manual selection)
- Per-file attribution history across the full timeline
- Attribution diff: see exactly which lines were AI-written vs human-written

### v1.3 — Multi-platform
- Linux and macOS support (remove Windows-only dependencies)
- Web dashboard for timeline browsing without the desktop app

### v2.0 — Team Mode
- Shared DeadVault server for team project history
- Multi-user attribution across a shared timeline
- GitHub/GitLab sync: push tagged versions as real commits

## Author

Built by [Yogesh Sangwan](https://voluntastech.com).

## License

MIT. See `LICENSE`.

## Verification

Stable solution build:

```powershell
dotnet build DeadVault.sln -m:1 -p:BuildInParallel=false
```

Run tests:

```powershell
dotnet test DeadVault.sln -m:1 -p:BuildInParallel=false
```
