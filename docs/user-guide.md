# PaletteShell User Guide

This is the user-facing overview for PaletteShell. For implementation details, use the focused maps
or split references in [README.md](README.md).

![PaletteShell — run your PowerShell scripts from the Windows Command Palette](../StoreAssets/PaletteShell_StoreHero_1920x1080.png)

**PaletteShell** is a [Windows Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/overview) extension that starts coding agent CLIs in the folder you pick and runs custom PowerShell scripts directly from the Command Palette. Open `claude` or `codex` in a project, transform clipboard text, generate GUIDs, format JSON, and automate your daily workflows — all without leaving your keyboard.

> 💡 Looking for ready-made scripts? Browse the community script library at **[paletteshell/PaletteShellScripts](https://github.com/paletteshell/PaletteShellScripts)** — also reachable from inside the palette via **"Browse community scripts"**, which opens the Script Manager when installed and falls back to the GitHub repo.

## 🌟 Features

- **🤖 Agent Sessions**: Start a coding agent CLI — `andrewcode`, `claude`, `agy`, `muse`, `codex`, or any command you add — in the folder you pick, in Windows Terminal. See [Agent Sessions](#-agent-sessions)
- **⭐ Favorite & Default Folders**: Keep the folders you work in one keystroke away, and choose where sessions start by default (your user folder unless you change it)
- **🔎 Folder Search**: Find project folders under your favorites and default folder by name, or type a path with completion
- **🚀 Quick Access**: Run PowerShell scripts directly from the Windows Command Palette
- **📋 Clipboard Utilities**: Transform and manipulate clipboard text with one keystroke
- **📝 Parameter Support**: Scripts with parameters get an interactive input form, generated from the script's own `param()` block
- **🎨 Rich Metadata**: Organize scripts with icons, descriptions, groups, and tags via PowerShell attributes
- **📄 Markdown Output**: Render a script's output as formatted Markdown inside the palette
- **🧮 Result Output**: Show a script's output as a single copyable result — press Enter to copy, "Run again" to regenerate; great for generators like a new GUID, password, or token
- **📜 List Output**: Turn a script into a search/pick provider — its stdout becomes a searchable list of items you can copy or open
- **⏳ Progress Feedback**: A "Running &lt;script&gt;…" spinner shows in the status bar while any script executes, so a slow script no longer looks frozen
- **📌 Pin to Top**: Pin your most-used scripts so they always sort to the top of the list
- **🗂️ Script Management**: Delete a script to the Recycle Bin (with confirmation) or reveal it in File Explorer — right from its context menu
- **☁ Community Script Manager**: Open the companion Script Manager app to browse, install, and update community scripts, with a GitHub fallback when the app isn't installed
- **✏️ Open in Editor**: Jump straight to any script's source in your `$EDITOR`/`$VISUAL` (Notepad by default)
- **⚡ Cross-Platform PowerShell**: Supports both PowerShell Core (`pwsh`) and Windows PowerShell (`powershell`)
- **🤖 AI-Ready**: Ships an `AGENTS.md` authoring spec into your scripts folder so AI coding agents can write compliant scripts for you on the fly
- **🔒 Security**: Runs in user context with optional admin elevation and an optional confirmation prompt per script
- **📁 Configurable Scripts Folder**: Choose where your scripts live the first time you open PaletteShell (or change it later from Settings) instead of being locked to one fixed folder
- **⚙️ Settings**: Set a default script host, default timeout, and preferred editor from PaletteShell's built-in Settings page

## 📖 Overview

PaletteShell turns a folder of PowerShell scripts into searchable, runnable commands inside the Windows Command Palette. Each `.ps1` file becomes a list item: PaletteShell reads metadata out of the script (its synopsis, description, icon, parameters, and behavior attributes) and presents it with a friendly title and subtitle. Selecting an item either runs the script immediately or — if the script declares parameters — opens a form to collect input first.

The first time you open PaletteShell it asks which folder to use for your scripts, suggesting **`Documents\PaletteShellScripts`**. Accept the suggestion or pick your own (e.g. a folder synced across machines); PaletteShell creates it and copies in a set of ready-to-use sample scripts plus the supporting `PaletteScriptAttributes.psm1` module. If you already used PaletteShell before this prompt existed, your existing `Documents\PaletteShellScripts` folder is picked up automatically — no prompt, nothing to redo. Add, edit, or remove files in your scripts folder at any time; use **"Reload scripts"** in the palette to pick up changes (this also picks up a folder you've changed from [Settings](extension-behavior-reference.md#settings)).


## 🚀 Getting Started

1. Install the extension (from the Microsoft Store, or by building and deploying the MSIX package — see [Building](#-building-from-source)).
2. Open the Command Palette and type **PaletteShell**.
3. The first time, choose a scripts folder (or accept the suggested `Documents\PaletteShellScripts`).
4. Browse the bundled sample scripts, choose **Create new script** to scaffold your own, or **Browse community scripts** to open the Script Manager or the [community repo](https://github.com/paletteshell/PaletteShellScripts).
5. Edit your scripts in your scripts folder and run **Reload scripts** to see changes.


## 🤖 Agent Sessions

Agent sessions open a coding agent CLI in a folder of your choice without leaving the keyboard.

1. Open the Command Palette and type the agent's name — **Claude Code**, **Codex**, **AndrewCode**, **Antigravity** (`agy`), **Muse** — or **Agent sessions** for the full list.
2. Press **Enter** to choose a folder:
   - With nothing typed you get your **default folder** (your user folder until you set another), your **favorites**, and **recent** folders.
   - Type to **search**: favorites and recent folders, plus project folders found up to three levels under your default folder and favorites (git repositories rank higher; `node_modules`, `bin`, `obj`, dot-folders and similar are skipped).
   - Type a **path** — `C:\src\`, `~\code\app`, `%USERPROFILE%\dev` — to list its subfolders. If the folder doesn't exist yet, PaletteShell offers to create it and start the agent there.
3. Press **Enter** on a folder to open the session. Each folder's context menu can start a different agent there, add or remove it as a favorite, make it the default folder, open it in File Explorer, or copy its path.

Each agent's own context menu (in Command Palette search or under **Agent sessions**) has **Start in …** shortcuts for the default folder and every favorite, so a frequent combination takes no folder search at all.

**Managing agents and folders**

- **Agent sessions → Add agent CLI** adds any command-line program; **Edit agent** / **Remove agent** are on each agent's context menu, and **Restore built-in agents** brings back any of the five defaults you removed.
- **Agent sessions → Favorite folders** adds, removes, and reorders favorites; Enter on a favorite asks which agent to start there.
- **Agent sessions → Default folder** sets (or clears) where sessions start by default.
- The same lists are plain text on the [Settings](extension-behavior-reference.md#settings) page: agents one per line as `Name | command and arguments` (e.g. `Claude Code | claude --model opus`), favorites one folder per line.

**How the session opens.** By default PaletteShell opens a new Windows Terminal window (or a new tab, if you prefer) running PowerShell 7 — or Windows PowerShell when PowerShell 7 isn't installed — which changes to the folder and runs the agent. The terminal stays open at a prompt in that folder when the agent exits. Your PowerShell profile loads as usual, so profile aliases and functions work as agent commands. If an agent shows "wasn't found on PATH", it will still be tried — but check the command, or use a full path. If Windows PowerShell's execution policy blocks an npm-installed CLI's `.ps1` shim, switch the session shell to PowerShell 7 or Command Prompt in Settings.

## 📦 Sample Scripts

The extension ships with ready-to-use scripts that double as working examples:

| Script | What it does |
|--------|--------------|
| `Base64-Encode` / `Base64-Decode` | Base64 encode/decode the clipboard text |
| `Clipboard-UrlEncode` / `Clipboard-UrlDecode` | URL encode/decode the clipboard text |
| `Json-Format` / `Json-Minify` | Pretty-print or minify clipboard JSON |
| `Clipboard-ToUpperCase` / `Clipboard-ToLowerCase` | Change the case of clipboard text |
| `Clipboard-SortLines` | Sort the lines on the clipboard |
| `Clipboard-RemoveDuplicateLines` | Remove duplicate lines |
| `Clipboard-TrimLines` | Trim whitespace from each line |
| `Clipboard-ToCSV` | Convert clipboard text to CSV |
| `Clear-TempFiles` | Delete old TEMP files after a confirmation prompt (demonstrates ConfirmBeforeRun on a parameterized script) |
| `Text-Transform` | Parameterized text transformation (demonstrates the input form) |
| `Generate-GUID` | Generate a new GUID and show it as a copyable result (demonstrates Result output) |
| `Clipboard-UnixTimestamp` | Insert/convert a Unix timestamp |
| `System-Report` | Render a system information report (demonstrates Markdown output) |
| `Export-ProcessList` | Snapshot running processes as CSV and open it in Excel (demonstrates File output) |
| `Open-TempFolder` | Open the user's temp folder in File Explorer (demonstrates Open output) |
| `Get-PublicIP` | Look up this machine's public IP and show it as a copyable result (demonstrates Result output) |
| `Git-Branches` | Type a repo folder path and pick one of its branches to copy (demonstrates List output as a live provider) |
| `Restart-Explorer` | Restart the Windows Explorer shell after a confirmation prompt (demonstrates ConfirmBeforeRun) |

For more, browse the community library at **[paletteshell/PaletteShellScripts](https://github.com/paletteshell/PaletteShellScripts)**.


## 🤝 Community Scripts

The **[PaletteShellScripts](https://github.com/paletteshell/PaletteShellScripts)** repository is a growing, community-maintained collection of scripts ready to drop into your scripts folder. Grab the ones you find useful, or contribute your own.

PaletteShell also pairs with the companion **Script Manager** application, which gives the community library a dedicated browsing, install, and update experience outside the Command Palette. Install it from the **[Microsoft Store](TODO_SCRIPT_MANAGER_STORE_URL)**, or review its source code in the **[Script Manager repository](TODO_SCRIPT_MANAGER_REPO_URL)**.

Use **"Browse community scripts"** from PaletteShell to launch Script Manager; if the app isn't installed yet, PaletteShell opens the GitHub repo instead.

## Need Help?

Check out the bundled sample scripts (in your scripts folder after first run) and the [community repo](https://github.com/paletteshell/PaletteShellScripts) for examples of common patterns and best practices.

## License
