# PaletteShell Extension

![PaletteShell - run your PowerShell scripts from the Windows Command Palette](StoreAssets/PaletteShell_StoreHero_1920x1080.png)

PaletteShell is a Windows Command Palette extension for two keyboard-first jobs:

- **Agent sessions** — start a coding agent CLI (`andrewcode`, `claude`, `agy`, `muse`, `codex`, or any command you add) in the folder you pick, in Windows Terminal.
- **Scripts** — turn a folder of PowerShell scripts into searchable commands: clipboard utilities, generators, Markdown reports, list providers, file exports, and other small automations.

This README is intentionally short so coding agents do not load the full project reference by default.

## Start Here

- Extension reference index: [docs/ExtensionReference.md](docs/ExtensionReference.md)
- Script authoring contract copied into user script folders as `AGENTS.md`: [docs/PaletteShellScripts.AGENTS.md](docs/PaletteShellScripts.AGENTS.md)
- Full script authoring reference copied beside it: [docs/PaletteShellScripts.Reference.md](docs/PaletteShellScripts.Reference.md)
- Focus maps: [parser](docs/parser-map.md), [execution](docs/execution-map.md), [UI](docs/ui-map.md), [agent sessions](docs/agent-sessions-map.md)
- Build, test, and debugging workflow: [CONTRIBUTING.md](CONTRIBUTING.md)
- Community scripts: [paletteshell/PaletteShellScripts](https://github.com/paletteshell/PaletteShellScripts)

## What It Does

Agent sessions:

- Lists your agent CLIs under **Agent sessions**, and (optionally) each one directly in Command Palette search — type `codex`, press Enter, pick a folder.
- Starts the agent in the folder you choose: your default folder (your user folder unless you set one), a favorite, a recent folder, a search result, or a typed path (with completion, and an offer to create a folder that doesn't exist yet).
- Search ranks favorites, recent folders, and project folders found a few levels under your default folder and favorites; git repositories rank higher.
- Opens Windows Terminal (a new window or a tab), or a console window, running PowerShell 7, Windows PowerShell, or cmd — and re-reads PATH so freshly installed CLIs are found.
- Agents, favorites, and the default folder are editable in the palette (add/edit/remove, favorite/unfavorite, reorder, set default) and as text on the Settings page.

Scripts:

- Discovers top-level `.ps1` files in the configured scripts folder.
- Parses script metadata without executing the script.
- Shows scripts as Command Palette items with title, description, icon, pinning, and context commands.
- Generates parameter forms from PowerShell `param(...)` blocks.
- Supports output modes for toast, clipboard, Markdown, result, list, open, file, and silent runs.
- Copies bundled sample scripts, `PaletteScriptAttributes.psm1`, a short `AGENTS.md`, and `PaletteShellScripts.Reference.md` into the user scripts folder.

## User Flow

Agent sessions:

1. Open Windows Command Palette and type an agent's name (`claude`, `codex`, …) or **Agent sessions**.
2. Press Enter on the agent, then pick a folder — or type to search, or type a path such as `C:\src\` or `~\code\new-app`.
3. Press Enter to open the session in Windows Terminal. The context menu (or the agent's own context menu) offers other agents, favorites, and set-as-default.
4. Add favorite folders and your own agent CLIs from **Agent sessions**, or edit them on the Settings page (one per line; agents as `Name | command and arguments`).

Scripts:

1. Install or build the extension.
2. Open Windows Command Palette and search for `PaletteShell`.
3. Choose a scripts folder on first run, or accept `Documents\PaletteShellScripts`.
4. Add or edit `.ps1` files in that folder.
5. Run `Reload scripts` in PaletteShell to pick up changes.

## Building

Requirements:

- .NET 9 SDK with the Windows 10.0.26100 platform.
- Windows 10 10.0.19041 or later.
- Windows Command Palette, currently part of PowerToys.

Common local loop:

```powershell
dotnet build PaletteShellExtension.sln -c Debug -p:Platform=x64
dotnet test PaletteShellExtension.sln -c Debug -p:Platform=x64
```

Use `ARM64` only when the task specifically targets ARM64. See [CONTRIBUTING.md](CONTRIBUTING.md) for Visual Studio debugging, sideload packaging, version bumps, and PR expectations.

## Project Map

| Path | Purpose |
|------|---------|
| `PaletteShellExtension/` | Extension source |
| `PaletteShellExtension.Tests/` | xUnit tests |
| `PaletteShellExtension/PowerShellScriptParser.cs` | Script metadata and parameter parser |
| `PaletteShellExtension/Classes/ScriptRunner.cs` | PowerShell process execution |
| `PaletteShellExtension/Classes/ScriptOutputHandler.cs` | Output-mode side effects |
| `PaletteShellExtension/Pages/PaletteShellExtensionPage.cs` | Main page, discovery, built-in commands |
| `PaletteShellExtension/Classes/AgentSessionService.cs` | Agent sessions state: agents, favorite/default/recent folders, folder index, launch |
| `PaletteShellExtension/Pages/AgentSessionsPage.cs`, `SessionFolderPickerPage.cs` | Agent sessions hub and per-agent folder picker |
| `PaletteShellExtension/Forms/` | Setup, parameter, and new-script forms |
| `PaletteShellExtension/SampleScripts/` | Bundled scripts copied to the user scripts folder |
| `docs/` | Detailed reference docs kept out of the default agent path |

## Script Authoring

Start with [docs/PaletteShellScripts.AGENTS.md](docs/PaletteShellScripts.AGENTS.md) for writing PaletteShell `.ps1` scripts. Use [docs/PaletteShellScripts.Reference.md](docs/PaletteShellScripts.Reference.md) when you need the full attribute, output mode, form mapping, or input-design contract.

Short version:

- Put `using module .\PaletteScriptAttributes.psm1` on line 1.
- Put all `[Script*]` attributes above `param(...)`.
- Provide `.SYNOPSIS`, `.DESCRIPTION`, and `.PARAMETER` help.
- Emit results on stdout for output modes that capture output; exit non-zero so failures open the shared details flow.
- Use clipboard, defaults, dropdowns, or live list providers instead of several heavy form inputs.

## License

Licensed under the [MIT License](LICENSE).
