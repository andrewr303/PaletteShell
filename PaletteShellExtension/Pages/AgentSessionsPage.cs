using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteShellExtension.Classes;
using PaletteShellExtension.Commands;
using PaletteShellExtension.Forms;
using System.Collections.Generic;

namespace PaletteShellExtension.Pages;

/// <summary>
/// The Agent sessions hub: every configured agent CLI (Enter picks a folder to start it in; the
/// context menu starts it straight in the default folder or a favorite, or edits/removes it),
/// plus favorite folders, the default folder, and adding agents.
/// </summary>
internal sealed partial class AgentSessionsPage : ListPage
{
    private readonly AgentSessionService _service;
    private readonly FavoriteFoldersPage _favoritesPage;

    public AgentSessionsPage(AgentSessionService service)
    {
        _service = service;
        _favoritesPage = new FavoriteFoldersPage(service);
        Title = "Agent sessions";
        Name = "Open";
        Icon = SessionIcons.Agent;
        Id = "AgentSessions";
        PlaceholderText = "Search agents and actions";
        service.Changed += (_, _) => RaiseItemsChanged();
    }

    /// <summary>The add (<paramref name="agent"/> null) or edit form for an agent CLI.</summary>
    public static FormContentPage AgentFormPage(AgentSessionService service, AgentCli? agent) =>
        new(agent is null ? "Add agent CLI" : $"Edit {agent.Name}",
            agent is null ? "AgentSessionAddAgent" : "AgentSessionEditAgent",
            agent is null ? SessionIcons.Add : SessionIcons.Edit,
            new AgentCliForm(service, agent));

    public override IListItem[] GetItems()
    {
        var items = new List<IListItem>();
        var agents = _service.Agents;
        SessionFolderPickerPage.Prune(agents);

        // Read PATH once for all the "is it installed?" checks below.
        var pathDirectories = AgentSessionLauncher.CurrentPathDirectories();

        foreach (var agent in agents)
        {
            var found = AgentSessionLauncher.IsOnPath(agent.Command, pathDirectories);
            var commands = SessionFolderItems.QuickStarts(_service, agent);
            commands.Add(new CommandContextItem(AgentFormPage(_service, agent)) { Title = "Edit agent", Icon = SessionIcons.Edit });
            commands.Add(new CommandContextItem(new RemoveAgentCliCommand(_service, agent)));

            items.Add(new ListItem(SessionFolderPickerPage.For(_service, agent))
            {
                Title = agent.Name,
                // Not found is only a hint: it may be a shell alias or function, so it still runs.
                Subtitle = found ? agent.CommandLine : $"⚠ '{agent.Command}' wasn't found on PATH — {agent.CommandLine}",
                Icon = SessionIcons.Agent,
                Section = "Agents",
                MoreCommands = [.. commands],
            });
        }

        if (agents.Count == 0)
        {
            items.Add(new ListItem(AgentFormPage(_service, null))
            {
                Title = "Add an agent CLI",
                Subtitle = "No agent CLIs are set up yet — add one, or restore the built-in list",
                Icon = SessionIcons.Add,
                Section = "Agents",
                MoreCommands = [RestoreBuiltInsItem()],
            });
        }

        var favoriteCount = _service.Favorites.Count;
        items.Add(new ListItem(_favoritesPage)
        {
            Title = "Favorite folders",
            Subtitle = favoriteCount == 0
                ? "Add the folders you start sessions in most"
                : $"{favoriteCount} {(favoriteCount == 1 ? "folder" : "folders")} — pick one, then an agent",
            Icon = SessionIcons.FavoriteFilled,
            Section = "Folders",
        });

        var configured = _service.ConfiguredDefaultFolder;
        items.Add(new ListItem(new FormContentPage("Default session folder", "AgentSessionDefaultFolder", SessionIcons.Home, new DefaultSessionFolderForm(_service)))
        {
            Title = "Default folder",
            Subtitle = configured is null
                ? $"{FolderPaths.UserFolder} (your user folder)"
                : FolderPaths.LooksMissing(configured) ? $"⚠ {configured} not found — using {FolderPaths.UserFolder}" : configured,
            Icon = SessionIcons.Home,
            Section = "Folders",
        });

        if (agents.Count > 0)
        {
            items.Add(new ListItem(AgentFormPage(_service, null))
            {
                Title = "Add agent CLI",
                Subtitle = "Any command-line program — it starts in the folder you pick",
                Icon = SessionIcons.Add,
                Section = "Setup",
                MoreCommands = [RestoreBuiltInsItem()],
            });
        }

        return [.. items];
    }

    private CommandContextItem RestoreBuiltInsItem() =>
        new(new CallbackCommand("Restore built-in agents", () =>
        {
            var added = _service.RestoreBuiltInAgents();
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = added == 0 ? "All built-in agents are already listed" : $"Restored {added} built-in {(added == 1 ? "agent" : "agents")}",
                Result = CommandResult.KeepOpen(),
            });
        }))
        {
            Icon = SessionIcons.Restore,
        };
}
