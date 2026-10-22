using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PaletteShellExtension.Classes;

/// <summary>Which terminal hosts an agent session.</summary>
internal enum SessionTerminal
{
    /// <summary>Windows Terminal when installed, else a console window.</summary>
    Auto,
    WindowsTerminal,
    Console,
}

/// <summary>Which shell runs the agent CLI inside the terminal.</summary>
internal enum SessionShell
{
    /// <summary>PowerShell 7 when installed, else Windows PowerShell.</summary>
    Auto,
    Pwsh,
    WindowsPowerShell,
    Cmd,
}

/// <summary>Whether Windows Terminal opens a new window or a tab in the most recent one.</summary>
internal enum TerminalWindowTarget
{
    NewWindow,
    NewTab,
}

/// <summary>The session launch preferences from Settings.</summary>
internal sealed record AgentLaunchSettings(
    SessionTerminal Terminal = SessionTerminal.Auto,
    SessionShell Shell = SessionShell.Auto,
    TerminalWindowTarget Window = TerminalWindowTarget.NewWindow,
    bool KeepOpen = true);

/// <summary>A fully resolved process to start. <see cref="Arguments"/> is the exact command line.</summary>
internal sealed record LaunchSpec(string FileName, string Arguments, string WorkingDirectory);

/// <summary>
/// Starts an agent CLI in a folder inside a terminal. <see cref="Build"/> turns an agent, folder,
/// and settings into a <see cref="LaunchSpec"/> without touching the system (so it's unit-tested);
/// <see cref="Plan"/> resolves the terminal and shell first, and <see cref="Start"/> runs it.
/// <para>
/// The agent is always started through a shell rather than directly: npm- and winget-installed
/// CLIs are <c>.cmd</c>/<c>.ps1</c> shims (or profile aliases) that only a shell resolves, and the
/// shell gives the session a prompt to fall back to when the agent exits. PowerShell gets its
/// command via <c>-EncodedCommand</c>, which sidesteps every layer of quoting (ours, wt.exe's
/// argument re-joining, and its <c>;</c> sub-command splitting) for arbitrary folders and arguments.
/// </para>
/// </summary>
internal static class AgentSessionLauncher
{
    /// <summary>Resolves the terminal and shell for <paramref name="settings"/> and builds the launch.
    /// Throws <see cref="ScriptRunner.ShellResolutionException"/> when a required shell is missing.</summary>
    public static LaunchSpec Plan(AgentCli agent, string folder, AgentLaunchSettings settings)
    {
        var windowsTerminal = settings.Terminal == SessionTerminal.Console ? null : FindWindowsTerminal();
        if (settings.Terminal == SessionTerminal.WindowsTerminal && windowsTerminal is null)
        {
            throw new ScriptRunner.ShellResolutionException(
                "Windows Terminal (wt.exe) wasn't found. Install it from the Microsoft Store, or set the session terminal to Auto.");
        }

        var (shellPath, shell) = ResolveShell(settings.Shell);
        return Build(agent, folder, settings, windowsTerminal, shellPath, shell);
    }

    /// <summary>
    /// Builds the launch for an already-resolved terminal and shell. With
    /// <paramref name="windowsTerminalPath"/> the session opens as a Windows Terminal tab/window;
    /// without it the shell gets its own console window (which Windows 11 hosts in Windows Terminal
    /// anyway when that's the default terminal app).
    /// </summary>
    public static LaunchSpec Build(
        AgentCli agent,
        string folder,
        AgentLaunchSettings settings,
        string? windowsTerminalPath,
        string shellPath,
        SessionShell shell)
    {
        var title = $"{agent.Name} · {FolderPaths.DisplayName(folder)}";
        var isCmd = shell == SessionShell.Cmd;

        if (windowsTerminalPath is null)
        {
            // cmd.exe reads its own command line raw (it doesn't use the MSVCRT rules), so its
            // /k payload goes in as-is; /s makes it strip exactly the outer pair of quotes.
            var consoleArgs = isCmd
                ? $"/s {(settings.KeepOpen ? "/k" : "/c")} \"{CmdCommand(agent, folder)}\""
                : WindowsCommandLine.Join(PowerShellArgs(agent, folder, title, settings.KeepOpen));
            return new LaunchSpec(shellPath, consoleArgs, folder);
        }

        // wt.exe [-w new|0] new-tab -d <folder> --title <title> <shell> <shell args>
        var wtArgs = new List<string>
        {
            "-w", settings.Window == TerminalWindowTarget.NewTab ? "0" : "new",
            "new-tab",
            // wt splits sub-commands on ';' even inside a quoted argument, so a folder containing
            // one is passed as '.' and resolved against the working directory instead.
            "-d", folder.Contains(';') ? "." : folder,
            "--title", title.Replace(';', ','),
            shellPath,
        };

        if (isCmd)
        {
            // wt re-joins the command it runs, wrapping any argument containing a space in plain
            // quotes, so the /k payload reaches cmd.exe as "<payload>" — which /s then unwraps.
            // wt would also split this argument at a ';', so escape it as wt's literal "\;".
            wtArgs.AddRange(["/s", settings.KeepOpen ? "/k" : "/c", CmdCommand(agent, folder).Replace(";", "\\;", StringComparison.Ordinal)]);
        }
        else
        {
            wtArgs.AddRange(PowerShellArgs(agent, folder, title, settings.KeepOpen));
        }

        return new LaunchSpec(windowsTerminalPath, WindowsCommandLine.Join(wtArgs), folder);
    }

    /// <summary>The script a PowerShell session runs: title the window, make sure we're in the
    /// folder (a profile that changes directory would otherwise win), then invoke the agent.
    /// Literals are single-quoted, so no folder or command can inject script.</summary>
    internal static string BuildPowerShellScript(AgentCli agent, string folder, string title)
    {
        var sb = new StringBuilder();
        sb.Append("$Host.UI.RawUI.WindowTitle = ").Append(PowerShellQuoting.SingleQuote(title)).Append('\n');
        sb.Append("Set-Location -LiteralPath ").Append(PowerShellQuoting.SingleQuote(folder)).Append('\n');
        sb.Append("& ").Append(PowerShellQuoting.SingleQuote(agent.Command));
        if (!string.IsNullOrWhiteSpace(agent.Arguments))
        {
            sb.Append(' ').Append(agent.Arguments.Trim());
        }

        sb.Append('\n');
        return sb.ToString();
    }

    // cmd.exe can't use a UNC path as its current directory (it falls back to the Windows
    // folder), so for a share it maps one with pushd first.
    private static string CmdCommand(AgentCli agent, string folder) =>
        folder.StartsWith(@"\\", StringComparison.Ordinal)
            ? $"pushd \"{folder}\" && {agent.CommandLine}"
            : agent.CommandLine;

    private static string[] PowerShellArgs(AgentCli agent, string folder, string title, bool keepOpen)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildPowerShellScript(agent, folder, title)));
        return keepOpen
            ? ["-NoLogo", "-NoExit", "-EncodedCommand", encoded]
            : ["-NoLogo", "-EncodedCommand", encoded];
    }

    /// <summary>Starts the process. Throws on failure so the caller can report it.</summary>
    public static void Start(LaunchSpec spec)
    {
        var psi = new ProcessStartInfo(spec.FileName, spec.Arguments)
        {
            // CreateProcess (not ShellExecute) so the session gets a refreshed PATH: Command
            // Palette is long-running, and a CLI installed since it started would otherwise be
            // "not recognized" in the new session. With no console of our own, the shell still
            // gets a fresh console window.
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = spec.WorkingDirectory,
        };

        var path = string.Join(Path.PathSeparator, CurrentPathDirectories());
        if (path.Length > 0)
        {
            psi.Environment["PATH"] = path;
        }

        using var process = Process.Start(psi);
    }

    /// <summary>Finds <c>wt.exe</c> on PATH or at its App Execution Alias location.</summary>
    internal static string? FindWindowsTerminal()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasDir = string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "Microsoft", "WindowsApps");
        return FindOnPath("wt.exe", aliasDir is null ? [] : [aliasDir]);
    }

    /// <summary>Resolves the shell setting to an executable path and the concrete shell kind.</summary>
    internal static (string Path, SessionShell Shell) ResolveShell(SessionShell preference)
    {
        switch (preference)
        {
            case SessionShell.Cmd:
                var comSpec = Environment.GetEnvironmentVariable("ComSpec");
                return (string.IsNullOrWhiteSpace(comSpec) ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : comSpec, SessionShell.Cmd);

            case SessionShell.Pwsh:
                return (ScriptRunner.ResolveShell(PaletteShellSettingsManager.HostPwsh), SessionShell.Pwsh);

            case SessionShell.WindowsPowerShell:
                return (ScriptRunner.ResolveShell(PaletteShellSettingsManager.HostPowerShell), SessionShell.WindowsPowerShell);

            default:
                var resolved = ScriptRunner.ResolveShell(PaletteShellSettingsManager.HostAuto);
                var isPwsh = Path.GetFileName(resolved).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
                return (resolved, isPwsh ? SessionShell.Pwsh : SessionShell.WindowsPowerShell);
        }
    }

    /// <summary>True when <paramref name="command"/> resolves to a file: an existing path, or a
    /// name found on the (refreshed) PATH with any PATHEXT or <c>.ps1</c> extension. A miss is
    /// only a hint — it could still be a shell alias or function — so callers warn, never block.
    /// Pass <paramref name="pathDirectories"/> when checking several commands, to read PATH once.</summary>
    public static bool IsOnPath(string command, IReadOnlyList<string>? pathDirectories = null)
    {
        try
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Append(".ps1")
                .ToArray();

            if (command.IndexOfAny(['\\', '/']) >= 0)
            {
                return File.Exists(command) || extensions.Any(ext => File.Exists(command + ext));
            }

            var dirs = pathDirectories ?? CurrentPathDirectories();
            return FindIn(dirs, command) is not null || extensions.Any(ext => FindIn(dirs, command + ext) is not null);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? FindOnPath(string fileName, IEnumerable<string> extraDirs) =>
        FindIn(CurrentPathDirectories().Concat(extraDirs), fileName);

    private static string? FindIn(IEnumerable<string> dirs, string fileName)
    {
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    /// <summary>
    /// The PATH a freshly opened terminal would see: machine + user PATH re-read from the registry
    /// (expanded), followed by any entries only this process has. Falls back to this process's PATH
    /// when the registry can't be read.
    /// </summary>
    internal static IReadOnlyList<string> CurrentPathDirectories()
    {
        var dirs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAll(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var raw in value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var dir = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
                if (dir.Length > 0 && seen.Add(dir))
                {
                    dirs.Add(dir);
                }
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var machine = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
                AddAll(machine?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
                using var user = Registry.CurrentUser.OpenSubKey("Environment");
                AddAll(user?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string);
            }
            catch (Exception ex)
            {
                Log.Warn($"Couldn't read PATH from the registry; using the process PATH: {ex.Message}");
            }
        }

        AddAll(Environment.GetEnvironmentVariable("PATH"));
        return dirs;
    }
}
