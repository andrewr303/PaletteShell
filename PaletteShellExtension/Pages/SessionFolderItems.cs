using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Commands;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PaletteShellExtension.Pages;

/// <summary>Row text and context menus shared by the Agent sessions pages, so a folder or agent
/// offers the same actions wherever it appears.</summary>
internal static class SessionFolderItems
{
    // Cap on per-agent "Start in <favorite>" shortcuts, so a long favorites list doesn't bury
    // the agent's other context commands.
    private const int MaxQuickStartFavorites = 8;

    /// <summary>A folder's row title: "Home" for the user folder (whose own name is just the
    /// user name), otherwise the folder's name.</summary>
    public static string Title(string folder) =>
        FolderPaths.Same(folder, FolderPaths.UserFolder) ? "Home" : FolderPaths.DisplayName(folder);

    /// <summary>Context menu for a folder row: start it with the other agents, favorite it, make it
    /// the default, open it in Explorer, or copy its path. <paramref name="defaultFolder"/> is
    /// passed in because callers build many rows at once and it can cost a disk probe.</summary>
    public static CommandContextItem[] FolderCommands(AgentSessionService service, string folder, AgentCli? currentAgent, string defaultFolder)
    {
        var items = new List<CommandContextItem>();
        foreach (var agent in service.Agents.Where(a => currentAgent is null || a.Key != currentAgent.Key))
        {
            items.Add(new CommandContextItem(new LaunchAgentSessionCommand(service, agent, folder)));
        }

        items.Add(new CommandContextItem(new ToggleFavoriteFolderCommand(service, folder)));
        if (!FolderPaths.Same(defaultFolder, folder))
        {
            items.Add(new CommandContextItem(new SetDefaultSessionFolderCommand(service, folder)));
        }

        items.Add(new CommandContextItem(new OpenFolderCommand(folder, "Open in File Explorer")));
        items.Add(new CommandContextItem(new CopyValueCommand(folder, "Copy path")));
        return [.. items];
    }

    /// <summary>One-step starts for an agent row — the default folder, then each favorite — so
    /// a frequent folder is a context-menu pick away without opening the folder picker.</summary>
    public static List<CommandContextItem> QuickStarts(AgentSessionService service, AgentCli agent)
    {
        var defaultFolder = service.DefaultFolder;
        var folders = new List<string> { defaultFolder };
        folders.AddRange(service.Favorites
            .Where(f => !FolderPaths.Same(f, defaultFolder))
            .Take(MaxQuickStartFavorites));

        return [.. folders.Select(folder => new CommandContextItem(new LaunchAgentSessionCommand(service, agent, folder))
        {
            Title = $"Start in {Title(folder)}",
            Subtitle = folder,
        })];
    }

    /// <summary>"just now", "5m ago", "3h ago", "2d ago", or a date for anything older.</summary>
    public static string RelativeTime(DateTime utc, DateTime nowUtc)
    {
        var age = nowUtc - utc;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}h ago"
            : age.TotalDays < 30 ? $"{(int)age.TotalDays}d ago"
            : utc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }
}
