using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PaletteShellExtension.Classes;

/// <summary>
/// A coding-agent CLI that Agent sessions can start in a folder — e.g. <c>claude</c> or
/// <c>codex</c>, optionally with arguments. Any command works; nothing here is agent-specific.
/// </summary>
/// <param name="Name">Display name shown in the palette ("Claude Code").</param>
/// <param name="Command">Program to run: a name resolved on PATH, or a full path.</param>
/// <param name="Arguments">Extra arguments, kept exactly as typed. They reach the agent through
/// the session shell, so they use that shell's syntax (PowerShell by default).</param>
internal sealed record AgentCli(string Name, string Command, string Arguments)
{
    /// <summary>The command plus its arguments, as the user would type it.</summary>
    public string CommandLine => WindowsCommandLine.JoinProgram(Command, Arguments);

    /// <summary>Identity for caching pages and keying top-level commands. Includes the command
    /// line, so editing an agent's arguments produces fresh pages rather than stale ones.</summary>
    public string Key => $"{Name}\u001F{CommandLine}";
}

/// <summary>One line of the stored agent list: its text, and the agent it defines (null for a
/// blank, comment, or unparseable line, which edits leave untouched).</summary>
internal sealed record AgentCliLine(string Text, AgentCli? Agent);

/// <summary>
/// Reads and writes the agent list stored in Settings. The format is one agent per line,
/// <c>Name | command and arguments</c> (the name and bar are optional), so the list stays
/// readable and editable straight from the Settings page as well as from the palette forms.
/// Blank lines and lines starting with <c>#</c> are ignored — and kept as written when the
/// palette edits the list (see <see cref="ReadLines"/>).
/// </summary>
internal static class AgentCliList
{
    private const char NameSeparator = '|';

    /// <summary>The agents a fresh install starts with, in the order they're listed.</summary>
    public static IReadOnlyList<AgentCli> BuiltIn { get; } =
    [
        new("AndrewCode", "andrewcode", ""),
        new("Claude Code", "claude", ""),
        new("Antigravity", "agy", ""),
        new("Muse", "muse", ""),
        new("Codex", "codex", ""),
    ];

    /// <summary>Settings text for <see cref="BuiltIn"/> — the setting's default value.</summary>
    public static string DefaultText { get; } = Format(BuiltIn);

    /// <summary>Parses the stored text. Unparseable lines are skipped rather than failing the
    /// whole list, and exact duplicates collapse to one entry.</summary>
    public static List<AgentCli> Parse(string? text)
    {
        var agents = new List<AgentCli>();
        foreach (var line in ReadLines(text))
        {
            if (line.Agent is { } agent && !agents.Contains(agent))
            {
                agents.Add(agent);
            }
        }

        return agents;
    }

    /// <summary>Splits the stored text into lines, keeping every line (comments and all) so an
    /// edit can rewrite just the lines it changes and <see cref="WriteLines"/> puts the rest back
    /// exactly as the user wrote them.</summary>
    public static List<AgentCliLine> ReadLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return [.. SplitLines(text).Select(line => new AgentCliLine(line, TryParseLine(line, out var agent) ? agent : null))];
    }

    public static string WriteLines(IEnumerable<AgentCliLine> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text));

    /// <summary>A freshly formatted line for <paramref name="agent"/>.</summary>
    public static AgentCliLine LineFor(AgentCli agent) => new(FormatLine(agent), agent);

    /// <summary>Parses one <c>Name | command args</c> line. Without a bar the whole line is the
    /// command line and the name is derived from the program.</summary>
    public static bool TryParseLine(string? line, out AgentCli agent)
    {
        agent = null!;
        var text = line?.Trim() ?? "";
        if (text.Length == 0 || text.StartsWith('#'))
        {
            return false;
        }

        string? name = null;
        var bar = text.IndexOf(NameSeparator);
        if (bar >= 0)
        {
            name = text[..bar].Trim();
            text = text[(bar + 1)..].Trim();
        }

        var (command, arguments) = WindowsCommandLine.SplitProgram(text);
        if (command.Length == 0)
        {
            return false;
        }

        agent = new AgentCli(string.IsNullOrWhiteSpace(name) ? DeriveName(command) : name, command, arguments);
        return true;
    }

    /// <summary>Formats agents back into the stored one-per-line text.</summary>
    public static string Format(IEnumerable<AgentCli> agents) =>
        string.Join(Environment.NewLine, agents.Select(FormatLine));

    private static string FormatLine(AgentCli agent) => $"{CleanName(agent.Name)} {NameSeparator} {agent.CommandLine}";

    /// <summary>Builds an agent from separate form fields, normalizing the name so it can't
    /// break the line format (no bar, no line breaks) and defaulting it from the command.</summary>
    public static AgentCli? Create(string? name, string? command, string? arguments)
    {
        // Accept a whole command line pasted into the command field ("claude --model opus"),
        // but an unquoted path to an existing file is the program even with spaces in it
        // (C:\Program Files\Tool\tool.exe).
        var trimmed = command?.Trim() ?? "";
        var (program, inlineArgs) = !trimmed.StartsWith('"') && trimmed.Any(char.IsWhiteSpace) && File.Exists(trimmed)
            ? (trimmed, "")
            : WindowsCommandLine.SplitProgram(trimmed);
        if (program.Length == 0)
        {
            return null;
        }

        var args = string.Join(' ', new[] { inlineArgs, SingleLine(arguments) }.Where(s => s.Length > 0));
        var cleanName = CleanName(name);
        return new AgentCli(cleanName.Length == 0 ? DeriveName(program) : cleanName, program, args);
    }

    /// <summary>A display name from the program: its file name without extension.</summary>
    public static string DeriveName(string command)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(command.Trim());
            return string.IsNullOrWhiteSpace(name) ? command.Trim() : name;
        }
        catch (ArgumentException)
        {
            return command.Trim();
        }
    }

    // No bar (it ends the name) and no leading '#' (the line would read as a comment).
    private static string CleanName(string? name) =>
        SingleLine(name).Replace(NameSeparator, '/').TrimStart('#').Trim();

    private static string SingleLine(string? value) =>
        string.Join(' ', SplitLines(value ?? "").Select(l => l.Trim()).Where(l => l.Length > 0));

    // The settings page's multiline box can hand back \r\n, \n, or bare \r line breaks.
    private static string[] SplitLines(string text) =>
        text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
}
