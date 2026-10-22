# Agent Sessions Map

Use this when changing Agent sessions: starting coding-agent CLIs (`claude`, `codex`, `agy`, …) in a
folder, the agent list, favorite/default/recent folders, folder search, or how sessions are launched.

## Main Files

- `PaletteShellExtension/Classes/AgentSessionService.cs` - state and operations (agents, favorites, default folder, recents, folder index, launch); raises `Changed`/`IndexUpdated` for pages.
- `PaletteShellExtension/Classes/AgentCli.cs` - `AgentCli` model and the `Name | command args` line format stored in Settings.
- `PaletteShellExtension/Classes/SessionFolders.cs` - path normalization (`~`, `%VAR%`, roots), the favorites line format, and the recent-folders store.
- `PaletteShellExtension/Classes/FolderSearch.cs` - bounded breadth-first folder index, search ranking, and typed-path completion.
- `PaletteShellExtension/Classes/AgentSessionLauncher.cs` - builds the Windows Terminal / console command line (pure `Build`) and starts it with a registry-refreshed PATH.
- `PaletteShellExtension/Classes/WindowsCommandLine.cs` - MSVCRT argument quoting and program/argument splitting.
- `PaletteShellExtension/Classes/PaletteShellSettingsManager.cs` - the `session*` settings; implements `IAgentSessionSettings`.
- `PaletteShellExtension/Pages/AgentSessionsPage.cs` - hub: agents, favorite folders, default folder, add agent.
- `PaletteShellExtension/Pages/SessionFolderPickerPage.cs` - per-agent folder picker (home list, search, path completion); one cached instance per agent.
- `PaletteShellExtension/Pages/FavoriteFoldersPage.cs`, `AgentPickerPage.cs` - manage favorites; folder-first agent choice.
- `PaletteShellExtension/Pages/SessionFolderItems.cs` - shared folder context menus and per-agent quick starts.
- `PaletteShellExtension/Forms/AgentCliForm.cs`, `FavoriteFolderForm.cs`, `DefaultSessionFolderForm.cs` - forms (wrapped by `Pages/FormContentPage.cs`).
- `PaletteShellExtension/PaletteShellExtensionCommandsProvider.cs` - top-level "Agent sessions" plus one root command per agent.

## Tests

- `PaletteShellExtension.Tests/AgentSessionLauncherTests.cs` - generated command lines, round-tripped through an argv parser.
- `PaletteShellExtension.Tests/AgentSessionServiceTests.cs` - state edits against `FakeSessionSettings`.
- `PaletteShellExtension.Tests/FolderSearchTests.cs`, `SessionFoldersTests.cs`, `AgentCliTests.cs`, `WindowsCommandLineTests.cs`, `AgentSessionFormsTests.cs`.

## Rules Of Thumb

- Agents and favorites live in multiline Settings text so they stay hand-editable; always go through `AgentSessionService` so pages and the provider hear about edits.
- Launch through a shell, never the agent directly: npm/winget CLIs are `.cmd`/`.ps1` shims or profile aliases.
- PowerShell receives its command via `-EncodedCommand`; never paste folders or arguments into a wt.exe or PowerShell command line unquoted. wt.exe splits on `;` even inside arguments.
- "Not found on PATH" is a warning, not a block — the command may be a shell alias or function.
- Keep folder walking bounded (depth, entry count, time) and off the host's calling thread.
