using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace PaletteShellExtension.Classes;

/// <summary>
/// Owns PaletteShell's extension-level settings (surfaced by the host as the provider's
/// settings page) and persists them to a JSON file under the extension's app-data folder.
/// </summary>
internal sealed class PaletteShellSettingsManager : JsonSettingsManager, IAgentSessionSettings
{
    public const string HostAuto = "auto";
    public const string HostPwsh = "pwsh";
    public const string HostPowerShell = "powershell";

    // Used when a script must be waited on to honor its output mode but declared no
    // [ScriptTimeout], and the user hasn't overridden the default (or entered garbage).
    private const int FallbackTimeoutMs = 30000;

    // Retention window for the %TEMP%\PaletteShell dir (failure reports + File-mode output).
    // Defaulted/clamped the same way the timeout is, so a typo'd setting can't disable
    // cleanup outright (0) or set an absurd window.
    private const int DefaultCleanupDays = 7;
    private const int MinCleanupDays = 1;
    private const int MaxCleanupDays = 365;

    public static PaletteShellSettingsManager Instance { get; } = new();

    /// <summary>The extension's app-data folder, holding settings.json and other local state.</summary>
    internal static string SettingsDirectory => Utilities.BaseSettingsPath("PaletteShellExtension");

    // ----- Agent sessions -----------------------------------------------------------------

    private readonly TextSetting _sessionAgents = new(
        "sessionAgents",
        "Agent CLIs",
        "One per line, as \"Name | command and arguments\" — for example \"Claude Code | claude --model opus\". Each one can be started in any folder from \"Agent sessions\".",
        AgentCliList.DefaultText)
    {
        Multiline = true,
    };

    private readonly TextSetting _sessionFavoriteFolders = new(
        "sessionFavoriteFolders",
        "Favorite session folders",
        "One folder per line. Listed first when you choose where to start a session, and searched (a few levels deep) for project folders.",
        string.Empty)
    {
        Multiline = true,
    };

    private readonly TextSetting _sessionDefaultFolder = new(
        "sessionDefaultFolder",
        "Default session folder",
        "Where sessions start when you don't pick a folder. Leave blank to use your user folder.",
        string.Empty);

    private readonly ChoiceSetSetting _sessionTerminal = new(
        "sessionTerminal",
        "Session terminal",
        "Where agent sessions open.",
        new List<ChoiceSetSetting.Choice>
        {
            new("Windows Terminal when installed (recommended)", "auto"),
            new("Windows Terminal", "wt"),
            new("Console window", "console"),
        });

    private readonly ChoiceSetSetting _sessionWindow = new(
        "sessionWindow",
        "Windows Terminal window",
        "Open each session in a new window, or as a new tab in the most recently used window.",
        new List<ChoiceSetSetting.Choice>
        {
            new("New window", "new"),
            new("New tab in the most recent window", "tab"),
        });

    private readonly ChoiceSetSetting _sessionShell = new(
        "sessionShell",
        "Session shell",
        "Shell that runs the agent CLI. Agent arguments use this shell's syntax.",
        new List<ChoiceSetSetting.Choice>
        {
            new("Auto — PowerShell 7, else Windows PowerShell (recommended)", "auto"),
            new("PowerShell 7 (pwsh)", HostPwsh),
            new("Windows PowerShell 5.1", HostPowerShell),
            new("Command Prompt (cmd)", "cmd"),
        });

    private readonly ToggleSetting _sessionKeepOpen = new(
        "sessionKeepOpen",
        "Keep the terminal open after the agent exits",
        "Leaves a shell prompt in the session folder when the agent CLI exits, instead of closing the tab.",
        true);

    private readonly ToggleSetting _sessionShowAgentsInRoot = new(
        "sessionShowAgentsInRoot",
        "Show each agent in Command Palette search",
        "Lists every agent CLI as its own command (type \"codex\" or \"claude\" to jump straight to its folder picker), in addition to \"Agent sessions\".",
        true);

    /// <summary>Raised after the user saves the Settings page. Programmatic edits don't raise it;
    /// <see cref="AgentSessionService"/> announces those itself.</summary>
    public event EventHandler? AgentSessionSettingsChanged;

    string IAgentSessionSettings.AgentsText
    {
        get => _sessionAgents.Value ?? string.Empty;
        set
        {
            _sessionAgents.Value = value;
            SaveSettings();
        }
    }

    string IAgentSessionSettings.FavoriteFoldersText
    {
        get => _sessionFavoriteFolders.Value ?? string.Empty;
        set
        {
            _sessionFavoriteFolders.Value = value;
            SaveSettings();
        }
    }

    string? IAgentSessionSettings.DefaultSessionFolder
    {
        get => string.IsNullOrWhiteSpace(_sessionDefaultFolder.Value) ? null : _sessionDefaultFolder.Value.Trim();
        set
        {
            _sessionDefaultFolder.Value = value ?? string.Empty;
            SaveSettings();
        }
    }

    AgentLaunchSettings IAgentSessionSettings.LaunchSettings => new(
        Terminal: _sessionTerminal.Value switch
        {
            "wt" => SessionTerminal.WindowsTerminal,
            "console" => SessionTerminal.Console,
            _ => SessionTerminal.Auto,
        },
        Shell: _sessionShell.Value switch
        {
            HostPwsh => SessionShell.Pwsh,
            HostPowerShell => SessionShell.WindowsPowerShell,
            "cmd" => SessionShell.Cmd,
            _ => SessionShell.Auto,
        },
        Window: _sessionWindow.Value == "tab" ? TerminalWindowTarget.NewTab : TerminalWindowTarget.NewWindow,
        KeepOpen: _sessionKeepOpen.Value);

    bool IAgentSessionSettings.ShowAgentsInRoot => _sessionShowAgentsInRoot.Value;

    // ----- Scripts --------------------------------------------------------------------------

    private readonly ChoiceSetSetting _defaultHost = new(
        "defaultHost",
        "Default script host",
        "Used for scripts that don't declare [ScriptHost(...)].",
        new List<ChoiceSetSetting.Choice>
        {
            new("Auto (recommended)", HostAuto),
            new("PowerShell 7 (pwsh)", HostPwsh),
            new("Windows PowerShell 5.1", HostPowerShell),
        });

    private readonly TextSetting _defaultTimeoutMs = new(
        "defaultTimeoutMs",
        "Default timeout (ms)",
        "Used for scripts that don't declare [ScriptTimeout(...)].",
        FallbackTimeoutMs.ToString(CultureInfo.InvariantCulture));

    private readonly TextSetting _preferredEditor = new(
        "preferredEditor",
        "Preferred editor",
        "Command or path used by \"Open in editor\". Leave blank to use $VISUAL, then $EDITOR, then Notepad.",
        string.Empty);

    private readonly TextSetting _scriptsFolder = new(
        "scriptsFolder",
        "Scripts folder",
        "Folder PaletteShell scans for .ps1 scripts. Changing this doesn't move existing scripts or pins — run \"Reload scripts\" afterward to pick it up.",
        string.Empty);

    private readonly ToggleSetting _cleanupTempEnabled = new(
        "cleanupTempEnabled",
        "Auto-clean temp reports/output",
        "Periodically delete old failure reports and \"File\" output-mode results from the temp folder (%TEMP%\\PaletteShell). These can contain script output and arguments.",
        true);

    private readonly TextSetting _cleanupTempDays = new(
        "cleanupTempDays",
        "Delete temp files older than (days)",
        "How long to keep files in %TEMP%\\PaletteShell before auto-clean removes them.",
        DefaultCleanupDays.ToString(CultureInfo.InvariantCulture));

    /// <summary>The configured default host: <c>"auto"</c>, <c>"pwsh"</c>, or <c>"powershell"</c>.</summary>
    public string DefaultHost => _defaultHost.Value ?? HostAuto;

    /// <summary>The user-configured default timeout, clamped to the same bounds a
    /// script-declared <c>[ScriptTimeout(...)]</c> gets, so a typo'd setting (an extra
    /// digit, say) can't turn every run into a multi-minute blocked COM call.</summary>
    public int DefaultTimeoutMs
    {
        get
        {
            if (!int.TryParse(_defaultTimeoutMs.Value, out var ms) || ms <= 0)
            {
                return FallbackTimeoutMs;
            }

            if (ms < PowerShellScriptParser.MinTimeoutMs)
            {
                Log.Warn($"Clamping default timeout setting ({ms}) to the {PowerShellScriptParser.MinTimeoutMs}ms minimum");
                return PowerShellScriptParser.MinTimeoutMs;
            }

            if (ms > PowerShellScriptParser.MaxTimeoutMs)
            {
                Log.Warn($"Clamping default timeout setting ({ms}) to the {PowerShellScriptParser.MaxTimeoutMs}ms maximum");
                return PowerShellScriptParser.MaxTimeoutMs;
            }

            return ms;
        }
    }

    /// <summary>The user's preferred editor command/path, or null when unset (fall back to env vars/Notepad).</summary>
    public string? PreferredEditor =>
        string.IsNullOrWhiteSpace(_preferredEditor.Value) ? null : _preferredEditor.Value.Trim();

    /// <summary>Whether the %TEMP%\PaletteShell dir is auto-cleaned when a new report/output is written.</summary>
    public bool CleanupTempEnabled => _cleanupTempEnabled.Value;

    /// <summary>Retention window (days) for %TEMP%\PaletteShell, clamped like
    /// <see cref="DefaultTimeoutMs"/> so a typo can't disable cleanup (0) or set an absurd window.</summary>
    public int CleanupRetentionDays
    {
        get
        {
            if (!int.TryParse(_cleanupTempDays.Value, out var days) || days <= 0)
            {
                return DefaultCleanupDays;
            }

            if (days < MinCleanupDays)
            {
                Log.Warn($"Clamping cleanup retention setting ({days}) to the {MinCleanupDays}-day minimum");
                return MinCleanupDays;
            }

            if (days > MaxCleanupDays)
            {
                Log.Warn($"Clamping cleanup retention setting ({days}) to the {MaxCleanupDays}-day maximum");
                return MaxCleanupDays;
            }

            return days;
        }
    }

    /// <summary>The configured scripts folder, or null when the user hasn't chosen one yet.</summary>
    public string? ScriptsFolder
    {
        get => string.IsNullOrWhiteSpace(_scriptsFolder.Value) ? null : _scriptsFolder.Value.Trim();
        set
        {
            _scriptsFolder.Value = value ?? string.Empty;
            SaveSettings();
        }
    }

    private PaletteShellSettingsManager()
    {
        // Instance is a static initializer, so anything thrown here would surface as a
        // TypeInitializationException at first touch — during COM activation — and kill
        // the process on every launch. Settings are a convenience, not a requirement:
        // on any failure, log and run this session with defaults instead of dying.
        try
        {
            var settingsDir = SettingsDirectory;
            Directory.CreateDirectory(settingsDir);
            FilePath = Path.Combine(settingsDir, "settings.json");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to prepare the settings folder; settings won't persist this session", ex);
        }

        // Agent sessions first: they're the most-used settings and the most likely to need
        // editing (the agent and favorite lists are editable here as well as from the palette).
        Settings.Add(_sessionAgents);
        Settings.Add(_sessionFavoriteFolders);
        Settings.Add(_sessionDefaultFolder);
        Settings.Add(_sessionTerminal);
        Settings.Add(_sessionWindow);
        Settings.Add(_sessionShell);
        Settings.Add(_sessionKeepOpen);
        Settings.Add(_sessionShowAgentsInRoot);

        Settings.Add(_defaultHost);
        Settings.Add(_defaultTimeoutMs);
        Settings.Add(_preferredEditor);
        Settings.Add(_scriptsFolder);
        Settings.Add(_cleanupTempEnabled);
        Settings.Add(_cleanupTempDays);

        try
        {
            QuarantineCorruptSettingsFile(FilePath);
            LoadSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load settings; continuing with defaults", ex);
        }

        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            AgentSessionSettingsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>
    /// If <c>settings.json</c> exists but isn't parseable JSON (torn write, disk hiccup),
    /// renames it to <c>settings.json.bad</c> so this and every future launch start clean
    /// with defaults, instead of tripping over the same corrupt file at each activation.
    /// The rename (rather than delete) keeps the evidence for diagnosis.
    /// </summary>
    internal static void QuarantineCorruptSettingsFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            JsonNode.Parse(File.ReadAllText(filePath));
        }
        catch (Exception parseEx)
        {
            Log.Error($"Settings file '{filePath}' is corrupt; moving it aside and starting with defaults", parseEx);
            try
            {
                File.Move(filePath, filePath + ".bad", overwrite: true);
            }
            catch (Exception moveEx)
            {
                Log.Warn($"Couldn't quarantine corrupt settings file: {moveEx.Message}");
            }
        }
    }
}
