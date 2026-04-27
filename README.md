# DeadVault

Snapshot-based version history for any folder. No Git required on the watched folder.

Watches project folders in the background, creates snapshots on file change, and stores them in a hidden internal Git repo. Includes an MCP server so AI tools can create versions, query diffs, and roll back with full attribution metadata.

## Components

| Component | What it does |
|---|---|
| `DeadVault.UI` | Desktop app. Add projects, browse timeline, restore versions. |
| `DeadVault.Agent` | Background watcher. Auto-snapshots on file change. |
| `DeadVault.McpServer` | MCP stdio server. Exposes tools to AI clients. |
| `DeadVault.Core` | Snapshot, diff, restore, attribution logic. |
| `DeadVault.Store` | JSON persistence layer. |

## Features

- Auto-snapshot on file change (configurable debounce)
- Manual snapshot with version bump (patch / minor / major / dev / custom)
- One-click restore with automatic pre-restore backup
- Diff view per commit with per-file line counts
- Attribution tracking: human vs AI per snapshot and per file
- `dvwm1-` watermark written to `.deadvault.attribution.json` per changed text file
- Query snapshots by author kind (`human`, `ai`, `mixed`)
- MCP server with full tool surface for AI clients

## Requirements

- Windows
- .NET 9 SDK

## Build and run

```powershell
# build everything
.\scripts\build.ps1

# run the desktop UI
dotnet run --project src\DeadVault.UI\DeadVault.UI.csproj

# run the background watcher separately
dotnet run --project src\DeadVault.Agent\DeadVault.Agent.csproj

# run the MCP server
dotnet run --project src\DeadVault.McpServer\DeadVault.McpServer.csproj
```

## Quick start

1. Open DeadVault, click **Add Project**, pick a folder.
2. Edit files in the folder. DeadVault snapshots automatically.
3. Click **Create Version** to tag a snapshot manually.
4. Open **Timeline** to diff, restore, or export.

## MCP setup (Claude Desktop)

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "deadvault": {
      "command": "C:\\path\\to\\DeadVault\\artifacts\\publish\\DeadVault.McpServer\\DeadVault.McpServer.exe"
    }
  }
}
```

Or using `dotnet run` (slower startup, no build required):

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

## MCP tools

| Tool | Read-only | Description |
|---|---|---|
| `list_projects` | yes | List all registered projects |
| `register_project` | no | Register a folder and start watching it |
| `list_versions` | yes | List snapshots for a project |
| `get_diff` | yes | Get full diff and attribution for a commit |
| `query_changes` | yes | Filter snapshots by author kind |
| `rollback` | no | Restore project to a prior snapshot |
| `create_version` | no | Create a tagged snapshot with bump kind and attribution |

### Attribution metadata on `create_version`

```
authorKind      "ai" | "human" | "mixed"
model           model name string (e.g. "claude-opus-4-5")
modelFamily     "claude" | "gpt" | "gemini" | "cursor" | etc.
clientName      MCP client identifier
```

DeadVault writes these into the Git commit trailer and the attribution manifest. Query later with `query_changes(author: "ai")`.

## Attribution

Snapshots default to `human` when created from the UI. MCP clients should pass explicit author metadata. Changed text files get a SHA256 watermark stored in `.deadvault.attribution.json`.

## Publish a release

```powershell
.\scripts\publish-release.ps1
```

Outputs to `artifacts\publish\DeadVault.UI`, `DeadVault.Agent`, and `DeadVault.McpServer`.

## Verify

```powershell
dotnet build DeadVault.sln -m:1 -p:BuildInParallel=false
dotnet test DeadVault.sln -m:1 -p:BuildInParallel=false
```

## Roadmap

**v1.1**
- VS Code extension with timeline sidebar
- CLI: `dvault snapshot`, `dvault diff`, `dvault restore`
- Cursor integration

**v1.2**
- Per-line human/AI attribution in diff view

**v1.3**
- Linux and macOS support
- Web dashboard

**v2.0**
- Multi-user shared server
- GitHub/GitLab sync

## License

MIT. See `LICENSE`.
