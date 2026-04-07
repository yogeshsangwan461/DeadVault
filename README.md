# DeadVault

Version history for any folder, without making it a Git repo.

DeadVault runs in the background, watches your project folders, and snapshots changes into a hidden timeline. You get fast restore, diffs, and export — and it never touches your actual working Git history. Built mainly for AI-assisted development where you want a safe rollback layer underneath whatever the AI is doing.

There's also an MCP server so AI tools like Claude can create versions, inspect diffs, and roll back on their own — with attribution metadata attached so you know what was AI-generated vs human-written.

## What's in the repo

- `DeadVault.UI` + `DeadVault.Agent` — the desktop app and background watcher
- `DeadVault.McpServer` — MCP stdio server for AI tools
- `DeadVault.Core` / `DeadVault.Store` — snapshot, diff, restore, attribution, and persistence
- `tests/DeadVault.Tests` — regression tests

## Features

- automatic snapshots on file change (configurable debounce)
- one-click restore with pre-restore backup
- diff view, export, tagging, semantic version bumps
- attribution manifest: tracks which files were human vs AI-written per snapshot
- MCP server with `create_version`, `query_changes`, `rollback`, and more
- deterministic watermark (`dvwm1-`) per text file, stored in `.deadvault.attribution.json`

## Requirements

- Windows
- .NET 9 SDK

## Running it

```powershell
# full solution build
.\scripts\build.ps1

# run the UI
dotnet run --project src\DeadVault.UI\DeadVault.UI.csproj

# run the background agent separately
dotnet run --project src\DeadVault.Agent\DeadVault.Agent.csproj

# run the MCP server
dotnet run --project src\DeadVault.McpServer\DeadVault.McpServer.csproj
```

## Quick start

1. Open DeadVault, click **Add Project**, pick a folder.
2. Make edits. DeadVault snapshots in the background automatically.
3. Click **Create Version** to snapshot and tag manually.
4. Open **Timeline** to diff, restore, export, or roll back.

## MCP integration

Add this to your Claude Desktop config:

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

Available tools: `list_projects`, `register_project`, `list_versions`, `get_diff`, `query_changes`, `rollback`, `create_version`.

The MCP server is the right surface when you want AI-generated changes to tag themselves. Anything coming through MCP can set `authorKind`, `model`, `modelFamily`, and `clientName` on the version — which DeadVault writes into the commit trailer and the attribution manifest for later querying.

## Attribution

Local app snapshots default to `human`. MCP clients can pass explicit author metadata. Changed text files get a `dvwm1-` watermark in `.deadvault.attribution.json` — a SHA256 fingerprint of content + path + author kind. You can query by author with `query_changes(author: "ai")` etc.

This is provenance recording, not tamper-proof signing. It's useful for knowing what happened, not for proving it in court.

## Publishing a release

```powershell
.\scripts\publish-release.ps1
```

Outputs to `artifacts\publish\DeadVault.UI`, `DeadVault.Agent`, and `DeadVault.McpServer`. Package the first two together for a desktop release ZIP.

## Roadmap

**v1.1 — Connectors**
- VS Code extension with timeline sidebar and inline restore
- CLI: `dvault snapshot`, `dvault diff`, `dvault restore`
- Cursor integration

**v1.2 — Attribution Intelligence**
- Per-line human/AI attribution in the diff view
- Attribution history across the full timeline

**v1.3 — Multi-platform**
- Linux and macOS (remove Windows-only deps)
- Web dashboard for timeline browsing

**v2.0 — Team Mode**
- Shared DeadVault server, multi-user attribution
- GitHub/GitLab sync: push tagged versions as real commits

## Verification

```powershell
dotnet build DeadVault.sln -m:1 -p:BuildInParallel=false
dotnet test DeadVault.sln -m:1 -p:BuildInParallel=false
```

## Author

Built by [Yogesh Sangwan](https://voluntastech.com).

## License

MIT. See `LICENSE`.
